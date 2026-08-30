/*
 *
 * DebugOverlay.cs
 *
 * Copyright 2026 Yuichi Yoshii
 *     吉井雄一 @ 吉井産業  you.65535.kir@gmail.com
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 *
 */

// =============================================================================
// デバッグ・オーバーレイ : 移動目的地に「光の柱」を表示（表示のみ・挙動非干渉）
//
//   OnMovePrefix の world/leader 確定直後(:66) から毎フレーム Sync を呼ぶ。
//   描画対象は「現在の真実」だけを読む:
//     - 緑 : リーダー現在地      leader.position
//     - 赤 : 追従中WP            PathFollowState._idx（DebugTryGetPath 経由）
//     - 青 : その他WP           上記以外の PathFollowState._wps[]
//     - 白線: WP を配列順(=経路順)に結ぶ折れ線（DebugOverlayConnectWaypoints）
//   経路が古い/無い場合は WP と接続線を出さず緑のみ（＝直線フォールバック中を映す）。
//
//   サイズ:
//     - 高さ/幅は設定ファイル（DebugOverlayHeight/Width）で調整。
//     - 自↔リーダーが近いほど一律に縮小（DebugOverlayAutoShrink）。ShrinkNearM 以下で
//       縮小開始、0m で 50%（下限固定）。camera≒self なので緑柱の見かけサイズ補償になる。
//
//   実装方針:
//     - LineRenderer プールを遅延生成。レンダーパイプライン/カメラコールバックに非依存。
//     - マテリアルは本体が Shader.Find 済みの "Unlit/Transparent Colored" を頂点カラーで使用。
//       1枚を全要素で共有し、色は startColor/endColor（頂点カラー）で出す。
//     - 後始末: ワールド再ロードで GameObject が破棄されると Unity の fake-null になるため
//       null チェックで自動再生成。無効時/リーダー喪失時は Hide() で消灯。
// =============================================================================

using System.Collections.Generic;
using CompanionAIVerify.Config;
using CompanionAIVerify.Positioning;
using UnityEngine;

namespace CompanionAIVerify.Utility.Debugging;

internal static class DebugOverlay
{
    private const float TopWidthRatio = 0.3f; // 先端幅 = 根元幅 × これ（固定テーパー）
    private const float MinShrinkScale = 0.5f; // 近接時の下限スケール（＝「半分まで」）

    private const float PathLineWidth = 0.05f; // WP接続線の基準太さ(m)
    private const float PathLineLift = 0.08f; // 接続線を地面から少し浮かせ z-fight 回避(m)

    private static readonly Color CLeader = new(0f, 1f, 0f, 0.6f); // 緑
    private static readonly Color CTarget = new(1f, 0f, 0f, 0.6f); // 赤（追従中WP）
    private static readonly Color COther = new(0.25f, 0.55f, 1f, 0.6f); // 青（その他WP）
    private static readonly Color CPathLine = new(1f, 1f, 1f, 0.35f); // 白（WP接続線）

    private static Material _mat;
    private static readonly List<LineRenderer> Pool = new();
    private static int _activeCount;
    private static LineRenderer _pathLine;

    // OnMovePrefix から毎フレーム（leader 確定後）。表示 or 消灯を config で分岐。
    internal static void Sync(EntityPlayerLocal self, EntityPlayer leader)
    {
        if (!Cfg.DebugOverlay)
        {
            Hide();
            return;
        }

        var scale = ComputeScale(self, leader);
        var used = 0;

        // 緑: リーダー現在地
        if (leader != null) SetPillar(used++, leader.position, CLeader, scale);

        // WP: follow-state の保持配列＋追従index を読むだけ（ロジック複製なし）。fresh でなければ出さない。
        var haveWps = PathFollowState.DebugTryGetPath(Cfg.PathStaleSec, out var wps, out var targetIdx);
        if (haveWps)
            for (var i = 0; i < wps.Length; i++)
                SetPillar(used++, wps[i], i == targetIdx ? CTarget : COther, scale);

        // 余ったプールを消灯
        for (var i = used; i < _activeCount; i++)
            if (Pool[i] != null)
                Pool[i].gameObject.SetActive(false);

        _activeCount = used;

        // WP接続線（順序＝配列順＝経路順）。2点以上あるときだけ。
        if (Cfg.DebugOverlayConnectWaypoints && haveWps && wps.Length >= 2)
            SetPathLine(wps, scale);
        else
            HidePathLine();
    }

    // 無効時/リーダー喪失時/トグルOFF時に呼ぶ。全要素を消灯（破棄はしない＝再表示が軽い）。
    internal static void Hide()
    {
        for (var i = 0; i < _activeCount; i++)
            if (Pool[i] != null)
                Pool[i].gameObject.SetActive(false);

        _activeCount = 0;
        HidePathLine();
    }

    // 自↔リーダー距離から一律スケールを算出。Near 以下で MinShrinkScale へ線形に縮む。
    private static float ComputeScale(EntityPlayerLocal self, EntityPlayer leader)
    {
        if (!Cfg.DebugOverlayAutoShrink || self == null || leader == null) return 1f;

        var flat = leader.position - self.position;
        flat.y = 0f;
        var near = Mathf.Max(0.01f, Cfg.DebugOverlayShrinkNearM);
        return Mathf.Lerp(MinShrinkScale, 1f, Mathf.Clamp01(flat.magnitude / near));
    }

    private static void SetPillar(int index, Vector3 basePos, Color color, float scale)
    {
        var lr = GetOrCreate(index);
        if (lr == null) return;

        var h = Cfg.DebugOverlayHeight * scale;
        var wBottom = Cfg.DebugOverlayWidth * scale;

        lr.positionCount = 2;
        lr.startWidth = wBottom;
        lr.endWidth = wBottom * TopWidthRatio;
        lr.startColor = color;
        lr.endColor = new Color(color.r, color.g, color.b, 0f); // 先端をαフェード
        lr.SetPosition(0, basePos);
        lr.SetPosition(1, basePos + Vector3.up * h);
        lr.gameObject.SetActive(true);
    }

    private static void SetPathLine(Vector3[] wps, float scale)
    {
        var lr = GetOrCreateSingle(ref _pathLine, "CAIV_DbgPathLine");
        if (lr == null) return;

        lr.positionCount = wps.Length;
        for (var i = 0; i < wps.Length; i++)
            lr.SetPosition(i, wps[i] + Vector3.up * PathLineLift);

        var w = PathLineWidth * scale;
        lr.startWidth = w;
        lr.endWidth = w;
        lr.startColor = CPathLine;
        lr.endColor = CPathLine;
        lr.gameObject.SetActive(true);
    }

    private static void HidePathLine()
    {
        if (_pathLine != null) _pathLine.gameObject.SetActive(false);
    }

    private static LineRenderer GetOrCreate(int index)
    {
        while (Pool.Count <= index) Pool.Add(null);
        if (Pool[index] != null) return Pool[index]; // fake-null なら下で再生成

        var lr = NewLineRenderer("CAIV_DbgPillar_" + index);
        Pool[index] = lr;
        return lr;
    }

    private static LineRenderer GetOrCreateSingle(ref LineRenderer field, string name)
    {
        if (field != null) return field;
        field = NewLineRenderer(name);
        return field;
    }

    private static LineRenderer NewLineRenderer(string name)
    {
        EnsureMaterial();
        if (_mat == null) return null; // シェーダ未取得（想定外）: 落とさず描画スキップ

        var go = new GameObject(name);
        var lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace = true;
        lr.positionCount = 2;
        lr.numCapVertices = 0;
        lr.alignment = LineAlignment.View; // 常にカメラを向く帯
        lr.material = _mat;
        return lr;
    }

    private static void EnsureMaterial()
    {
        if (_mat != null) return;

        // 本体が現に Shader.Find で引いている確認済みシェーダ（GameManager:931）。
        // 頂点カラーを乗せる半透明Unlit。_MainTex 未指定でも白1x1が入るよう明示。
        var sh = Shader.Find("Unlit/Transparent Colored");
        if (sh == null) return;

        _mat = new Material(sh) { mainTexture = Texture2D.whiteTexture };
    }
}