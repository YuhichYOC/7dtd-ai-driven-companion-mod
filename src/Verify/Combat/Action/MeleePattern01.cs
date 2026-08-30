/*
 *
 * MeleePattern01.cs
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

using CompanionAIVerify.Combat.Scene;
using CompanionAIVerify.Config;
using CompanionAIVerify.Positioning;

namespace CompanionAIVerify.Combat.Action;

/*
 * 格闘アクション実装パターン 1
 */
internal class MeleePattern01 : ICombatAction
{
    private InfoHolder _i;

    string ICombatAction.Name => "MeleePattern01";

    void ICombatAction.Run(InfoHolder i)
    {
        _i = i;

        if (!_i.ApprovementValidator.EngageApproved() || !_i.TargetValidator.TargetIsValid())
        {
            _i.ReleaseOperation.Run();
            return;
        }

        _i.Ranged = false;
        _i.FireMax = Cfg.RangedMaxEngageMeters;

        EngageRange.LogTick(_i.Self, _i.Target.Target);

        _i.SwitchOperation.Run();
        if (_i.SwitchOperation.Switched) return;

        // ★ ( 1 ) 交戦の手前で bFirstPersonView を実ログ確定
        if (_i.ReachValidator.TargetInReach()) _i.FpvOperation.Run();

        // ★ ( 2 ) 近接交戦
        // 3D エイム ( ピッチ込み ) -> press 駆動スイング
        _i.FaceOperation.Run();

        // ★ v0.8(B)-A
        // 近接レイをターゲットのチェストへ自動補正させる
        _i.AimOperation.MeleeAim();

        // press
        _i.SwingOperation.Run();
    }
}