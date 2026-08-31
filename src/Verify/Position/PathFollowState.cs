/*
 *
 * PathFollowState.cs
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
// navigation スライス3 : クライアント側 経路追従ステート（受信経路 → 移動目標）
//
//   PathWire.OnChunkClient が再結合した Vector3[]＋status を SetPath で受け取り保持する。
//   CompanionExecutor（F8ドライバ）の follow posture が TryGetMoveTarget を呼んで
//   「今向かうべきウェイポイント」を得る。
//
//   方針:
//     - 経路が無い/古い/消化済み → false を返す。呼び側は既存の「リーダーへ直線」に自然フォールバック。
//     - 新経路到着時は最近傍ウェイポイントへ index を再シードし、後戻りを最小化。
//     - 到達済みウェイポイントは半径＋高さ許容で飛ばして次点へ。
//     - 到達判定は ASPPathNavigate.pathFollow(:91-143) の考え方に倣う（半径＋高さ差許容）。
//     - status(REACHED/APPROACHING/STUCK) は呼び側へ渡すのみ（本スライスでは追従は全status共通。
//       STUCK でも部分経路を辿らせることでコンパニオンを動かし、ホスト側再評価で解消を促す）。
// =============================================================================

using UnityEngine;

namespace CompanionAIVerify.Position;

internal static class PathFollowState
{
    private static Vector3[] _wps;
    private static float _recvTime = -999f;
    private static int _idx;
    private static bool _reseed;

    internal static string LastStatus { get; private set; } = "";

    internal static bool HasFresh(float staleSec)
    {
        return _wps != null && _wps.Length > 0 && Time.time - _recvTime <= staleSec;
    }

    // デバッグ・オーバーレイ専用の読み取り口
    // 保持中の経路と追従 index を返すだけ ( 状態不変 )
    //   targetIdx は前フレームの TryGetMoveTarget が確定した「向かうべきWP」
    //   Sync は follow 分岐より前 ( : 66 ) で呼ばれるため、新経路到着直後の 1 フレームだけ targetIdx が旧値になり得る ( 表示のみ・害なし )
    internal static bool DebugTryGetPath(float staleSec, out Vector3[] wps, out int targetIdx)
    {
        wps = _wps;
        targetIdx = _idx;
        return _wps != null && _wps.Length > 0 && Time.time - _recvTime <= staleSec;
    }

    // PathWire.OnChunkClient から呼ばれる（クライアント側）
    internal static void SetPath(Vector3[] wps, string status)
    {
        _wps = wps;
        LastStatus = status ?? "";
        _recvTime = Time.time;
        _reseed = true;
    }

    // 追従の移動目標を返す。経路が無い/古い/消化済みなら false（呼び側は直線フォールバック）。
    internal static bool TryGetMoveTarget(Vector3 selfPos, float arriveRadius, float heightTol,
        float staleSec, out Vector3 target, out string status)
    {
        target = Vector3.zero;
        status = LastStatus;

        if (_wps == null || _wps.Length == 0 || Time.time - _recvTime > staleSec)
            return false;

        if (_reseed)
        {
            _reseed = false;
            _idx = NearestIndex(selfPos); // 新経路: 最近傍から辿り後戻りを最小化
        }

        // 到達済みウェイポイントを飛ばす（水平半径＋高さ許容）
        while (_idx < _wps.Length)
        {
            var d = _wps[_idx] - selfPos;
            var h = Mathf.Abs(d.y);
            d.y = 0f;
            if (d.magnitude <= arriveRadius && h <= heightTol) _idx++;
            else break;
        }

        if (_idx >= _wps.Length) return false; // 経路消化 → 最終接近は既存の直線に委ねる
        target = _wps[_idx];
        return true;
    }

    private static int NearestIndex(Vector3 p)
    {
        var best = 0;
        var bestSq = float.MaxValue;
        for (var i = 0; i < _wps.Length; i++)
        {
            var d = _wps[i] - p;
            d.y = 0f;
            var sq = d.sqrMagnitude;
            if (sq < bestSq)
            {
                bestSq = sq;
                best = i;
            }
        }

        return best;
    }
}