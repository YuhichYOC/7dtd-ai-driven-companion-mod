/*
*
* ReleaseOperation.cs
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

namespace CompanionAIVerify.Combat.Operate
{
    internal class ReleaseOperation
    {
        private InfoHolder _i;
        private LogInfoHolder _li;

        internal ReleaseOperation(InfoHolder i, LogInfoHolder li)
        {
            _i = i;
            _li = li;
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
            if (_i.SwingOperation.Pressed)
            {
                _i.Self.Attack(true); // release ( スイングの後始末 )
                _i.SwingOperation.Pressed = false;
            }
            // v0.8(B)-A: 張っていた aim-assist の attackTarget を解除。
            //   client では SetAttackTarget(null,0) も entityDistributer.SendPacket(EntityAlive:5932)で NRE になるため
            //   フィールドを直接 null 代入する。attackTargetTime は元々 0 のまま＝失効パス(3367-)にも入らない。
            if (_i.AimAssistSet)
            {
                _i.Self.AttackTarget = null; // client-safe な直接解除
                _i.AimAssistSet = false;
            }
        }

        // 遠距離のトリガーも安全に開放 ( 脅威消失 / 無効化 / 切替時に呼ぶ )
        // ADS も解除
        private void ReleaseRanged()
        {
            if (_i.TriggerOperation.Pressed)
            {
                _i.Self.Attack(true);
                _i.TriggerOperation.Pressed = false;
            }
            else if (_i.DrawOperation.Pressed)
            {
                // ★ [bow] 弓ドロー中は release = 発射になる
                // 開放要求は「キャンセル ( 矢を消費しない引き戻し ) 」へ振り替えて暴発を防ぐ
                // CancelAction は m_bActivated 中に triggerReleased して活性を落とす
                // 矢は ConsumeAmmo を通らないため消費されない ( ItemActionCatapult : 176-196 )
                if (_i.DrawOperation.BowDrawing)
                {
                    var bow = _i.GetHeldCatapult();
                    if (_i.DrawOperation.BowHolding)
                    {
                        bow.Item1.CancelAction(bow.Item2);
                    }
                    _i.DrawOperation.BowDrawing = false; // 武器が既に切替済み ( cat == null ) でもフラグは必ず落とす ( StopHolding が旧弓を CancelAction 済み )
                }
                _i.Self.Attack(true);
                _i.DrawOperation.Pressed = false;
            }
            _i.ADSOperation.Run(false);
        }
    }
}
