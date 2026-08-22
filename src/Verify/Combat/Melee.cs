/*
*
* Melee.cs
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

namespace CompanionAIVerify.Combat
{
    internal static class Melee
    {
        private static float _reach;

        private static float _targetDistance;

        private static bool _inRange;

        private static bool _aboutToEngage;

        private static bool _aimAssistSet;

        internal static void Run(EntityPlayerLocal self, in ThreatInfo threat)
        {
            Setup(self, threat);
            if (!CheckClearToEngage(self)) return;
            FaceTarget3D(self, threat);
            AimAssist(self);
            Attack(self);
        }

        private static void Setup(EntityPlayerLocal self, in ThreatInfo threat)
        {
            _reach = GetAttackReach(self);
            _targetDistance = MathF.Sqrt(threat.DistSq);
        }

        private static bool CheckClearToEngage(EntityPlayerLocal self)
        {
            _inRange = d <= reach + Cfg.ReachBuffer;
            // ★ ( 1 ) 交戦の手前で bFirstPersonView を実ログ確定
            _aboutToEngage = _inRange;
            if (_aboutToEngage) ConfirmFirstPersonView(self);
            if (!_inRange)
            {
                ReleaseIfPressed(self);
                return false;
            }
            return true;
        }

        // ★ ( 2 ) 近接交戦 : 3D エイム ( ピッチ込み ) -> press 駆動スイング
        //
        // World 内で制御キャラクターの実際の向きを指定する
        private static void FaceTarget3D(EntityPlayerLocal self, in ThreatInfo threat)
        {
            FaceTarget3D(self, threat.Target);
        }

        // ★ v0.8(B)-A: 近接レイをターゲットのチェストへ自動補正させる。
        //   ItemActionDynamic.GetExecuteActionTarget は attackTarget!=null のとき
        //   ray を getChestPosition() 方向へ差し替える（ItemActionDynamic:327-330）。
        //   これで FaceTarget3D の平面精度に依存せず命中が安定する。
        //
        //   ※ client では SetAttackTarget() を使えない：内部で world.entityDistributer.SendPacket を叩くが
        //     entityDistributer は IsServer 時のみ生成される（World:468-477）ため client では null → NRE。
        //     さらに attackTargetTime>0 にすると自動失効パス(EntityAlive:3367-3376)も同じ null を踏む。
        //     → public フィールドへ直接代入し、attackTargetTime は 0 のまま（失効パスに入らせない）。
        //       解除は ReleaseIfPressed で直接 null 代入。redirect は attackTarget を読むだけ(GetAttackTarget:5890)なので十分。
        //       ダメージ同期は Attack()→DamageEntity()→NetPackageDamageEntity 経由で別途成立（attackTarget 非依存）。
        //
        // Face により向きが決まった前提で、どこを殴るのか？という観点
        private static void AimAssist(EntityPlayerLocal self)
        {
            if (Cfg.MeleeAimAssist)
            {
                self.attackTarget = threat.Target; // EntityAlive:716 (public field) — client-safe
                _aimAssistSet = true;
            }
        }

        private static void Attack(EntityPlayerLocal self)
        {
            if (self.Attack(false)) // press。ケイデンスは canStartAttack の APM 律速が制御
            {
                _attackPressed = true;
                if (Time.time >= _nextEngageLogTime)
                {
                    _nextEngageLogTime = Time.time + Cfg.LogThrottleSec;
                    Log.Out($"[CompanionAI] engage: swing {threat.Kind} {threat.State} d={d:0.0}m reach={reach:0.0}m");
                }
            }
        }

#region -- CombatDriver から移植・抽象クラスへ移動できそうなもの --

        // 保持アイテムの実効リーチ。EngageRange.Read が Dynamic melee(=ItemActionDynamic.Range/RangeDefault)を
        // 正しく解決する。旧実装は基底 ItemAction.Range を読んでいたため、Dynamic melee のリーチを取れず
        // 2.4m 武器を 2.0m フォールバック扱いしていた（実ログで range=2.4 と確認済み）。取れなければ 2.0m。
        private static float GetAttackReach(EntityPlayerLocal self)
        {
            EngageRange.Info er = EngageRange.Read(self);
            if (er.valid && er.range > 0.01f) return er.range;
            return 2.0f;
        }

        // 交戦の手前で bFirstPersonView を実ログ。初回 or 変化時のみ出力。
        private static void ConfirmFirstPersonView(EntityPlayerLocal self)
        {
            bool fpv = self.bFirstPersonView;
            if (_fpvLogged && fpv == _lastFpv) return;

            _fpvLogged = true;
            _lastFpv   = fpv;
            Log.Out($"[CompanionAI] engage-precheck: bFirstPersonView={fpv} TPCam={self.TPCameraCheckResult} camPassed={self.TPCameraCheckPassed}");

            if (!fpv && Cfg.ForceFirstPerson)
            {
                self.SetFirstPersonView(true, false); // spawn 経路の誤設定を自己修復
                Log.Out("[CompanionAI] engage-precheck: forced bFirstPersonView=true (ForceFirstPerson).");
            }
        }

        internal static void ReleaseIfPressed(EntityPlayerLocal self)
        {
            if (_attackPressed)
            {
                self.Attack(true); // release ( スイングの後始末 )
                _attackPressed = false;
            }
            // v0.8(B)-A : 張っていた aim-assist の attackTarget を解除。
            //   client では SetAttackTarget(null, 0) も entityDistributer.SendPacket(EntityAlive:5932) で NRE になるため
            //   フィールドを直接 null 代入する。attackTargetTime は元々 0 のまま = 失効パス(3367-)にも入らない。
            if (_aimAssistSet)
            {
                self.attackTarget = null; // client-safe な直接解除
                _aimAssistSet = false;
            }
        }

        // ピッチ込みで対象中心付近を狙う（低い脅威にも当てるため y を潰さない）。
        // 変換式は facing と同一 ( EPL : 2310, 248-252 )。camera 経由で攻撃レイが操舵される。
        private static void FaceTarget3D(EntityPlayerLocal self, EntityAlive target)
        {
            Vector3 eye = self.position + Vector3.up * 1.5f;   // 概算カメラ高
            Vector3 aim = target.position + Vector3.up * 0.9f; // 概算胴中心
            Vector3 dir = aim - eye;
            if (dir.sqrMagnitude < 1e-6f) return;

            Vector3 euler = Quaternion.LookRotation(dir.normalized, Vector3.up).eulerAngles;
            euler.x *= -1f; // ピッチ反転 ( EPL : 239, 251 )
            self.SetRotation(euler);
        }

#endregion -- CombatDriver から移植・抽象クラスへ移動できそうなもの --

    }
}