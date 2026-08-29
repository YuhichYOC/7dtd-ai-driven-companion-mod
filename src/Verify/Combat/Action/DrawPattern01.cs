/*
 *
 * DrawPattern01.cs
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
using CompanionAIVerify.Log;
using CompanionAIVerify.Positioning;

namespace CompanionAIVerify.Combat.Action;

/*
 * 弓用アクション実装パターン 1
 */
internal class DrawPattern01 : ICombatAction
{
    private InfoHolder _i;

    string ICombatAction.Name => "DrawPattern01";

    void ICombatAction.Run(InfoHolder i)
    {
        _i = i;

        if (!_i.ApprovementValidator.EngageApproved() || !_i.TargetValidator.TargetIsValid())
        {
            _i.ReleaseOperation.Run();
            return;
        }

        _i.Ranged = true;
        _i.FireMax = Cfg.RangedMaxEngageMeters;

        EngageRange.LogTick(_i.Self, _i.Target.Target);

        _i.SwitchOperation.Run();
        if (_i.SwitchOperation.Switched) return;

        // ★ ( 1 ) 交戦の手前で bFirstPersonView を実ログ確定
        // ★ v0.8(C) 射程ゲート
        if (!_i.ReachValidator.TargetInReach())
        {
            _i.FpvOperation.Run();
            _i.ReleaseOperation.Run();
            Logger.LogTargetOutOfRange(_i);
            return;
        }

        // --- 発砲ドライバ ( v0.6.0 ) ---
        if (!_i.ApprovementValidator.FireApproved())
        {
            _i.ReleaseOperation.Run();
            Logger.LogRangedProhibited(_i);
            return;
        }

        // ★ ( 1 ) 射線が対象に届く狙点を探す
        _i.AimOperation.RangeAim();

        // body / 視覚トラッキング ( 見た目の照準 ) はホールド中も維持
        _i.AimOperation.RangeRotate();

        if (!_i.AimOperation.Shootable) // ★ 撃たない : 遮蔽 / FF / 空。理由をログしてホールド
        {
            _i.ReleaseOperation.Run();
            Logger.LogShootableNotFound(_i);
            return;
        }

        // ★ v0.8(D) 友軍射線ガード
        if (Cfg.FriendlyFireGate && _i.ShootableValidator.FriendlyInLineOfFire())
        {
            _i.ReleaseOperation.Run();
            Logger.LogFriendlyInLineOfFire(_i);
            return;
        }

        // ★ ( 2 ) 発砲準備 : ADS + カメラを狙点へスナップ
        _i.AimOperation.RangeAimSnapCamera();

        // ★ 弓, ドローを含めた射撃処理
        // クロスボウはドロー操作がないので Trigger で処理する
        _i.DrawOperation.Init();
        if (!_i.ApprovementValidator.BowDrawApproved())
        {
            // ドロー無効時は弓を撃たない
            // ドローなしでは strain ≈ 0 で実用にならない
            // ドロー中なら安全に引き戻す
            _i.DrawOperation.CancelDrawing(true);
            return;
        }

        if (_i.DrawOperation.Drawing && !_i.DrawOperation.ActionActivated)
            // ゲーム側キャンセル
            //   Catapult : 141 - 145 等
            //     - 矢切れ
            //     - 武器切替
            //     - TP カメラ NG
            // で活性が落ちたら状態同期
            _i.DrawOperation.CancelDrawing(false);

        if (!_i.DrawOperation.Drawing)
        {
            _i.DrawOperation.StartDraw();
            return;
        }

        // Drawing 中
        _i.DrawOperation.Draw();
    }
}