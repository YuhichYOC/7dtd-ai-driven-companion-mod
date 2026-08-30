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
//   経路が古い/無い場合は WP を出さず緑のみ（＝直線フォールバック中を素直に映す）。
//
//   実装方針:
//     - LineRenderer プールを遅延生成。レンダーパイプライン/カメラコールバックに非依存。
//     - マテリアルは本体が Shader.Find 済みの "Unlit/Transparent Colored" を頂点カラーで使用。
//       1枚を全柱で共有し、色は LineRenderer の startColor/endColor（頂点カラー）で出す。
//     - 後始末: ワールド再ロードで GameObject が破棄されると Unity の fake-null になるため
//       GetOrCreate の null チェックで自動再生成。無効時/リーダー喪失時は Hide() で消灯。
// =============================================================================

using System.Collections.Generic;
using CompanionAIVerify.Config;
using CompanionAIVerify.Positioning;
using UnityEngine;

namespace CompanionAIVerify.Utility.Debugging;

internal static class DebugOverlay
{
    // 見た目（デバッグ専用の固定値。config 化しない）
    private const float PillarHeight = 4.0f; // 柱の高さ(m)
    private const float WidthBottom = 0.18f; // 根元の幅(m)
    private const float WidthTop = 0.05f; // 先端の幅(m)
    private const float BaseAlpha = 0.6f; // 根元のα（先端は0へフェード＝光柱感）

    private static readonly Color CLeader = new(0f, 1f, 0f, BaseAlpha); // 緑
    private static readonly Color CTarget = new(1f, 0f, 0f, BaseAlpha); // 赤（追従中WP）
    private static readonly Color COther = new(0.25f, 0.55f, 1f, BaseAlpha); // 青（その他WP）

    private static Material _mat;
    private static readonly List<LineRenderer> Pool = new();
    private static int _activeCount;

    // OnMovePrefix から毎フレーム（leader 確定後）。表示 or 消灯を config で分岐。
    internal static void Sync(EntityPlayer leader)
    {
        if (!Cfg.DebugOverlay)
        {
            Hide();
            return;
        }

        var used = 0;

        // 緑: リーダー現在地
        if (leader != null) SetPillar(used++, leader.position, CLeader);

        // WP: follow-state が今保持している配列＋追従index を読むだけ（ロジック複製なし）。
        //   fresh でなければ WP は出さない（＝直線フォールバック中を映す）。
        if (PathFollowState.DebugTryGetPath(Cfg.PathStaleSec, out var wps, out var targetIdx))
            for (var i = 0; i < wps.Length; i++)
                SetPillar(used++, wps[i], i == targetIdx ? CTarget : COther);

        // 余ったプールを消灯
        for (var i = used; i < _activeCount; i++)
            if (Pool[i] != null)
                Pool[i].gameObject.SetActive(false);

        _activeCount = used;
    }

    // 無効時/リーダー喪失時/トグルOFF時に呼ぶ。全柱を消灯（破棄はしない＝再表示が軽い）。
    internal static void Hide()
    {
        for (var i = 0; i < _activeCount; i++)
            if (Pool[i] != null)
                Pool[i].gameObject.SetActive(false);

        _activeCount = 0;
    }

    private static void SetPillar(int index, Vector3 basePos, Color color)
    {
        var lr = GetOrCreate(index);
        if (lr == null) return;

        lr.startWidth = WidthBottom;
        lr.endWidth = WidthTop;
        lr.startColor = color;
        lr.endColor = new Color(color.r, color.g, color.b, 0f); // 先端をαフェード
        lr.SetPosition(0, basePos);
        lr.SetPosition(1, basePos + Vector3.up * PillarHeight);
        lr.gameObject.SetActive(true);
    }

    private static LineRenderer GetOrCreate(int index)
    {
        while (Pool.Count <= index) Pool.Add(null);

        var lr = Pool[index];
        if (lr != null) return lr; // fake-null（ワールド再ロードで破棄）なら再生成へ

        EnsureMaterial();
        if (_mat == null) return null; // シェーダ未取得（想定外）: 落とさず描画スキップ

        var go = new GameObject("CAIV_DbgPillar_" + index);
        lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace = true;
        lr.positionCount = 2;
        lr.numCapVertices = 0;
        lr.alignment = LineAlignment.View; // 常にカメラを向く帯（柱として視認しやすい）
        lr.material = _mat;
        Pool[index] = lr;
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