/*
 *
 * FollowPattern01.cs
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
 * 一番単純なリーダー追従移動パターンの実装
 * 脅威が近くにいるときの「脅威の方向を向く」振る舞いをしない
 */
internal class FollowPattern01 : IPositionPattern
{
    private InfoHolder _i;

    string IPositionPattern.Name => "FollowPattern01";

    void IPositionPattern.Run(InfoHolder i)
    {
        _i = i;

        if (_i.PositionValidator.NearLeader())
        {
            _i.HaltOperator.Run();
            return;
        }

        // 既定は直線でリーダーへ
        // 経路が届いていれば中間ウェイポイントへ向かう ( navigation スライス3 )
        var moveTarget = _i.Leader.position;
        var pathActive = false;
        if (Cfg.PathFollow
            && PathFollowState.TryGetMoveTarget(
                _i.Self.position,
                Cfg.WaypointArriveM,
                Cfg.WaypointHeightTolM,
                Cfg.PathStaleSec,
                out var wpTarget,
                out _))
        {
            moveTarget = wpTarget;
            pathActive = true;
        }

        // 戦闘中は脅威を向く ( 既存優先 )
        // 非戦闘の経路追従中のみ進行方向を向く
        if (pathActive)
        {
            var tdir = moveTarget - _i.Self.position;
            tdir.y = 0.0f;
            if (tdir.sqrMagnitude > 0.001f)
            {
                _i.SteerOperator.Run(moveTarget, tdir, _i.PositionValidator.Dist > Cfg.RunMeters);
                return;
            }
        }

        _i.SteerOperator.Run(moveTarget, _i.PositionValidator.Flat, _i.PositionValidator.Dist > Cfg.RunMeters);
    }
}