/*
 *
 * TriggerPattern01.cs
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
 * 銃撃 ( クロスボウ含む ) アクション実装パターン 1
 */
internal class TriggerPattern01 : ICombatAction
{
    private InfoHolder _i;

    string ICombatAction.Name => "TriggerPattern01";

    void ICombatAction.Run(InfoHolder i)
    {
        _i = i;

        if (!_i.ApprovementValidator.EngageApproved() || !_i.TargetValidator.TargetIsValid())
        {
            _i.ReleaseOperation.Run();
            return;
        }

        EngageRange.LogTick(_i.Self, _i.Target.Target);

        _i.SwitchOperation.Run();
        if (_i.SwitchOperation.Switched) return;

        // ★ ( 1 ) 交戦の手前で bFirstPersonView を実ログ確定
        // ★ v0.8(C) 射程ゲート
        if (_i.ReachValidator.TargetInReach())
        {
            _i.FpvOperation.Run();
            _i.ReleaseOperation.Run();
            _i.LogInfoHolder.Logger.LogTargetOutOfRange(_i);
            return;
        }

        // --- 発砲ドライバ ( v0.6.0 ) ---
        if (!_i.ApprovementValidator.FireApproved())
        {
            _i.ReleaseOperation.Run();
            _i.LogInfoHolder.Logger.LogRangedProhibited(_i);
            return;
        }

        // ★ ( 1 ) 射線が対象に届く狙点を探す
        _i.AimOperation.RangeAim();

        // body / 視覚トラッキング ( 見た目の照準 ) はホールド中も維持
        _i.AimOperation.RangeRotate();

        if (!_i.AimOperation.Shootable) // ★ 撃たない : 遮蔽 / FF / 空。理由をログしてホールド
        {
            _i.ReleaseOperation.Run();
            _i.LogInfoHolder.Logger.LogShootableNotFound(_i);
            return;
        }

        // ★ v0.8(D) 友軍射線ガード
        if (Cfg.FriendlyFireGate && _i.ShootableValidator.FriendlyInLineOfFire())
        {
            _i.ReleaseOperation.Run();
            _i.LogInfoHolder.Logger.LogFriendlyInLineOfFire(_i);
            return;
        }

        // ★ ( 2 ) 発砲準備 : ADS + カメラを狙点へスナップ
        _i.AimOperation.RangeAimSnapCamera();

        // ★ ( 3 ) フルオート判定して駆動
        _i.TriggerOperation.Run();
    }
}