/*
 *
 * PositionValidator.cs
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
 * See the License for the specific language governing permissions and
 * limitations under the License.
 *
 */

using CompanionAIVerify.Config;
using CompanionAIVerify.Position.Scene;
using UnityEngine;

namespace CompanionAIVerify.Position.Validation;

internal class PositionValidator
{
    private readonly InfoHolder _i;

    private Vector3 _flat;

    private Vector3 _toTarget;

    internal PositionValidator(InfoHolder i)
    {
        _i = i;
    }

    internal Vector3 Flat => _flat;
    internal float Dist { get; private set; }

    internal Vector3 LookDir { get; private set; }

    internal Vector3 ToTarget => _toTarget;
    internal Vector3 LookFwd { get; private set; }

    // リーダーに近い = 追従移動する必要がない場合 true を返す
    // 移植元メソッド RunFollowPositioning の副作用 flat がほしい場合は Flat で受け取ることができる
    // 移植元メソッド RunFollowPositioning の副作用 dist がほしい場合は Dist で受け取ることができる
    internal bool NearLeader()
    {
        _flat = _i.Leader.position - _i.Self.position;
        _flat.y = 0.0f;
        Dist = _flat.magnitude;
        return Dist <= Cfg.StandoffMeters;
    }

    // リーダーに近い = 追従移動する必要がない場合 true を返す
    // リーダー追従移動 & 近くの脅威に視線を向けるパターンでのオーバーロード
    //   LookDir の計算に NearLeader 計算途中の _flat が必要なためこの実装を分けた
    // 移植元メソッド RunFollowPositioning の副作用 flat がほしい場合は Flat で受け取ることができる
    // 移植元メソッド RunFollowPositioning の副作用 dist がほしい場合は Dist で受け取ることができる
    // 移植元メソッド RunFollowPositioning の副作用 lookDir がほしい場合は LookDir で受け取ることができる
    internal bool NearLeader(Vector3 threatPosition)
    {
        _flat = _i.Leader.position - _i.Self.position;
        _flat.y = 0.0f;
        Dist = _flat.magnitude;
        LookDir = _flat;
        var toThreat = threatPosition - _i.Self.position;
        toThreat.y = 0.0f;
        if (toThreat.sqrMagnitude > 0.001f) LookDir = toThreat;
        return Dist <= Cfg.StandoffMeters;
    }

    // 目的地に到達した場合 true を返す
    // 移植元メソッド Steer の副作用 toTarget がほしい場合は ToTarget で受け取ることができる
    internal bool Arrived(Vector3 moveTarget)
    {
        _toTarget = moveTarget - _i.Self.position;
        _toTarget.y = 0.0f;
        return _toTarget.sqrMagnitude < 0.01f;
    }

    // Arrived 呼び出しの後に実行すること
    // 移動方向へ向き直る ( FaceOperator.Run ) 必要がある場合 true を返す
    // 移植元メソッド Steer の副作用 lookFwd がほしい場合は LookFwd で受け取ることができる
    internal bool ShouldRotate(Vector3 lookDir)
    {
        var ld = lookDir;
        ld.y = 0.0f;
        if (ld.sqrMagnitude > 0.001f)
        {
            LookFwd = ld.normalized;
            return true;
        }

        LookFwd = _toTarget.normalized;
        return false;
    }
}