/*
*
* Swing.cs
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
    internal class SwingOperation
    {
        private InfoHolder _i;
        private LogInfoHolder _li;

        internal SwingOperation(InfoHolder i, LogInfoHolder li)
        {
            _i = i;
            _li = li;
        }

        // ★ v0.8(B)-A
        // 近接レイをターゲットのチェストへ自動補正させる
        //   ItemActionDynamic.GetExecuteActionTarget は attackTarget!=null のとき
        //   ray を getChestPosition() 方向へ差し替える ( ItemActionDynamic : 327-330 )
        //   これで FaceTarget3D の平面精度に依存せず命中が安定する
        //
        //   ※ client では SetAttackTarget() を使えない ... 内部で world.entityDistributer.SendPacket を叩くが
        //     entityDistributer は IsServer 時のみ生成される ( World : 468-477 ) ため client では null -> NRE
        //     さらに attackTargetTime > 0 にすると自動失効パス ( EntityAlive : 3367-3376 ) も同じ null を踏む
        //     -> public フィールドへ直接代入し、attackTargetTime は 0 のまま ( 失効パスに入らせない )
        //       解除は ReleaseIfPressed で直接 null 代入。redirect は attackTarget を読むだけ ( GetAttackTarget : 5890 ) なので十分
        //       ダメージ同期は Attack() -> DamageEntity() -> NetPackageDamageEntity 経由で別途成立 ( attackTarget 非依存 )
        internal void Run()
        {
            if (Cfg.MeleeAimAssist)
            {
                _i.Self.attackTarget = _i.Target.Target; // EntityAlive : 716 ( public field ) — client-safe
                _i.AimAssistSet = true;
            }
            if (_i.Self.Attack(false)) // press。ケイデンスは canStartAttack の APM 律速が制御
            {
                _i.FirePressed = true;
                _li.Log.LogMeleeSwing(_i);
            }
        }
    }
}
