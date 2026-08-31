/*
 *
 * FollowPattern02.cs
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

namespace CompanionAIVerify.Position.Pattern;

/*
 * リーダー追従移動パターンの実装
 * 脅威が近くにいるとき脅威の方向を向く
 */
internal class FollowPattern02 : IPositionPattern
{
    private InfoHolder _i;

    string IPositionPattern.Name => "FollowPattern02";

    void IPositionPattern.Run(InfoHolder i)
    {
        _i = i;

        if (_i.PositionValidator.NearLeader(_i.Threat.Target.position))
        {
            _i.FaceOperator.Run(_i.PositionValidator.LookDir);
            _i.HaltOperator.Run();
            return;
        }

        // 既定は直線でリーダーへ
        // 経路が届いていれば中間ウェイポイントへ向かう ( navigation スライス3 )
        var moveTarget = _i.Leader.position;
        if (Cfg.PathFollow
            && PathFollowState.TryGetMoveTarget(
                _i.Self.position,
                Cfg.WaypointArriveM,
                Cfg.WaypointHeightTolM,
                Cfg.PathStaleSec,
                out var wpTarget,
                out _))
            moveTarget = wpTarget;

        _i.SteerOperator.Run(moveTarget, _i.PositionValidator.Flat, _i.PositionValidator.Dist > Cfg.RunMeters);
    }
}