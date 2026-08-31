/*
 *
 * ReleaseOperator.cs
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

using CompanionAIVerify.Action.Scene;

namespace CompanionAIVerify.Action.Operation;

internal class ReleaseOperator
{
    private readonly InfoHolder _i;

    internal ReleaseOperator(InfoHolder i)
    {
        _i = i;
    }

    internal void Run()
    {
        if (_i.Ranged)
        {
            ReleaseRanged();
            return;
        }

        ReleaseMelee();
    }

    private void ReleaseMelee()
    {
        if (_i.SwingOperator.Pressed)
        {
            _i.Self.Attack(true); // release ( スイングの後始末 )
            _i.SwingOperator.Pressed = false;
        }

        // v0.8(B)-A: 張っていた aim-assist の attackTarget を解除。
        //   client では SetAttackTarget(null,0) も entityDistributer.SendPacket(EntityAlive:5932)で NRE になるため
        //   フィールドを直接 null 代入する。attackTargetTime は元々 0 のまま＝失効パス(3367-)にも入らない。
        if (_i.AimOperator.AimAssistSet)
        {
            _i.Self.attackTarget = null; // client-safe な直接解除
            _i.AimOperator.AimAssistSet = false;
        }
    }

    // 遠距離のトリガーも安全に開放 ( 脅威消失 / 無効化 / 切替時に呼ぶ )
    // ADS も解除
    private void ReleaseRanged()
    {
        if (_i.TriggerOperator.Pressed)
        {
            _i.Self.Attack(true);
            _i.TriggerOperator.Pressed = false;
        }
        else if (_i.DrawOperator.Pressed)
        {
            // ★ [bow] 弓ドロー中は release = 発射になる
            // 開放要求は「キャンセル ( 矢を消費しない引き戻し ) 」へ振り替えて暴発を防ぐ
            // CancelAction は m_bActivated 中に triggerReleased して活性を落とす
            // 矢は ConsumeAmmo を通らないため消費されない ( ItemActionCatapult : 176-196 )
            if (_i.DrawOperator.Drawing)
            {
                var bow = _i.GetHeldCatapult();
                if (_i.DrawOperator.BowHolding) bow.Item1.CancelAction(bow.Item2);
                _i.DrawOperator.Drawing =
                    false; // 武器が既に切替済み ( cat == null ) でもフラグは必ず落とす ( StopHolding が旧弓を CancelAction 済み )
            }

            _i.Self.Attack(true);
            _i.DrawOperator.Pressed = false;
        }

        _i.AdsOperator.Run(false);
    }
}