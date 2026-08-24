/*
*
* Trigger.cs
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
    internal static class Trigger
    {
        private static float _reach;

        private static float _targetDistance;

        private static bool _inRange;

        private static bool _aboutToEngage;

        private static float _fireMax;

        private static float _nextHoldLogTime; // ホールド理由ログの throttle

        internal static void Run(EntityPlayerLocal self, in ThreatInfo threat)
        {
            Setup(self, threat);
            if (!CheckClearToEngage(self)) return;
            Attack(self, threat);
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
            // ★ ( 3 ) 遠距離 : 発砲スライス
            if (!Cfg.EnableRangedFire)
            {
                ReleaseFireIfPressed(self);
                if (_targetDistance <= Cfg.RangedMaxEngageMeters && Time.time >= _nextEngageLogTime)
                {
                    _nextEngageLogTime = Time.time + 1.0f;
                    Log.Out($"[CompanionAI] engage: ranged holding within reach (d={_targetDistance:0.0}m) — fire disabled.");
                }
                return false;
            }
            return true;
        }

        // ★ ( 3 ) 遠距離 : 発砲スライス
        private static void Attack(EntityPlayerLocal self, in ThreatInfo threat)
        {
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

        // ★ v0.8(C): 射程ゲート。グローバル上限(RangedMaxEngageMeters)に加え、
        //   武器固有の実効射程でも「弾が届かない距離」を弾く。fireMax = min(グローバル上限, 実効射程×安全係数)。
        //   実効射程 = EngageRange.Read().range（ranged では GetRange()＝MaxRange 適用後の発射射程, ItemActionRanged:1376）。
        //   Slice A 実測で shotgun range≈10 なのに d≈20 で撃って弾が届かない問題を解消する。
        //   d は feet-to-feet、実際の弾は camera→aimPoint なので安全係数(既定0.85)で余裕を持たせる。
        //   ※ [bow] 弓も ItemActionRanged 派生なので GetRange が取れる。ただし矢は放物線弾道で直線射程とズレる
        //     （fireShot は無効化: Launcher:120-125）。落下/リードの弾道補正は本スライスのスコープ外。
        private static bool VerifyTargetInConfigRange(EntityPlayerLocal self)
        {
            float fireMax = Cfg.RangedMaxEngageMeters;
            EngageRange.Info erC = EngageRange.Read(self);
            if (erC.valid && erC.isRanged && erC.range > 0.01f)
                fireMax = Mathf.Min(fireMax, erC.range * Cfg.RangedRangeSafety);

            if (_targetDistance > fireMax)
            {
                ReleaseFireIfPressed(self);
                if (Time.time >= _nextHoldLogTime)
                {
                    _nextHoldLogTime = Time.time + Cfg.LogThrottleSec;
                    Log.Out($"[CompanionAI] hold: {threat.Kind} id={threat.Target.entityId} d={d:0.0}m > fireMax={fireMax:0.0}m " +
                            $"(range={erC.range:0.0} x{Cfg.RangedRangeSafety:0.00}, cap={Cfg.RangedMaxEngageMeters:0.0})");
                }
                return false;
            }
            return true;
        }

        private static void SetTriggerCam()
        {
            EntityAlive tgt = threat.Target;
            float headLift  = tgt.getHeadPosition().y - tgt.position.y;

            // カメラ実ワールド位置（=弾の原点）。視差補正の基準。
            Vector3 camWorld = (Cfg.AimFromCameraOrigin && self.playerCamera != null)
                ? self.playerCamera.transform.position + Origin.position
                : self.getHeadPosition();

            // ★ (1) 射線が対象に届く狙点を探す
            Vector3 aimPoint;
            string aimMode, bodyPart, reason;
            bool shootable;
            if (Cfg.RequireShootable)
            {
                shootable = ResolveShootableAim(self, tgt, camWorld, headLift,
                                                out aimPoint, out aimMode, out bodyPart, out reason);
            }
            else
            {
                // ゲート無効時は従来のハイブリッド狙点をそのまま採用（検証なし）
                bool useHead = headLift >= Cfg.HeadAimMinLift;
                aimPoint = useHead ? tgt.getHeadPosition() : tgt.position + Vector3.up * tgt.scaledExtent.y;
                aimMode  = useHead ? "head" : "center";
                bodyPart = "-"; reason = "ok"; shootable = true;
            }

            // body/視覚トラッキング（見た目の照準）はホールド中も維持
            SetAimRotation(self, aimPoint - self.getHeadPosition());
        }

        // ★ シュータブル解決: 候補狙点(頭/胴中心/腹)を順に自前レイキャストし、
        //   対象コライダーに実際に当たる最初の点を返す。全滅なら理由(block/OTHER/sky)付きで false。
        //   fireShot と同じ SetModelLayer(2)＋Voxel.Raycast(world,ray,range,-538751005,8,0) を使用。
        private static bool ResolveShootableAim(EntityPlayerLocal self, EntityAlive tgt, Vector3 camWorld,
                                                float headLift, out Vector3 aimPoint, out string mode,
                                                out string part, out string reason)
        {
            aimPoint = tgt.position; mode = "none"; part = "-"; reason = "sky";

            Vector3 head   = tgt.getHeadPosition();
            Vector3 center = tgt.position + Vector3.up * tgt.scaledExtent.y;
            Vector3 belly  = tgt.getBellyPosition();
            if (headLift >= Cfg.HeadAimMinLift) { _candPts[0] = head;   _candNm[0] = "head";   _candPts[1] = center; _candNm[1] = "center"; }
            else                                { _candPts[0] = center; _candNm[0] = "center"; _candPts[1] = head;   _candNm[1] = "head";   }
            _candPts[2] = belly; _candNm[2] = "belly";

            World world = self.world;
            bool haveReason = false;
            int ml = self.GetModelLayer();
            self.SetModelLayer(2); // 自己を射線から除外（fireShot と同じ）
            try
            {
                for (int i = 0; i < 3; i++)
                {
                    Vector3 dir = _candPts[i] - camWorld;
                    if (dir.sqrMagnitude < 1e-6f) continue;
                    float range = dir.magnitude + 1.0f;
                    if (!Voxel.Raycast(world, new Ray(camWorld, dir.normalized), range, -538751005, 8, 0f))
                    {
                        if (!haveReason) { haveReason = true; reason = "sky"; }
                        continue;
                    }
                    WorldRayHitInfo info = Voxel.voxelRayHitInfo.Clone();
                    Entity e = ItemActionAttack.FindHitEntityNoTagCheck(info, out string bp);
                    if (e != null && e.entityId == tgt.entityId)
                    {
                        aimPoint = _candPts[i]; mode = _candNm[i]; part = string.IsNullOrEmpty(bp) ? "body" : bp; reason = "ok";
                        return true;
                    }
                    if (!haveReason)
                    {
                        haveReason = true;
                        if (e != null) reason = "OTHER id=" + e.entityId;
                        else reason = "block:" + (string.IsNullOrEmpty(info.tag)
                            ? (info.transform != null ? info.transform.name : "?") : info.tag);
                    }
                }
            }
            finally { self.SetModelLayer(ml); }
            return false;
        }

        // 意図方向(aimDir)で body/camera を向ける。ranged ショットは GetLookRay(camera) 由来なので
        // SetRotation がカメラ Angle を更新する（ItemActionRanged:1579, EPL:2310）。
        // ただしカメラ transform 反映は遅延するため、ラグ対策は RangedStep 側で別途スナップする。
        private static void SetAimRotation(EntityPlayerLocal self, Vector3 aimDir)
        {
            if (aimDir.sqrMagnitude < 1e-6f) return;
            Vector3 euler = Quaternion.LookRotation(aimDir.normalized, Vector3.up).eulerAngles;
            euler.x *= -1f; // ピッチ反転（EPL:239, 251）
            self.SetRotation(euler);
        }

        // ★ v0.8(D): 友軍射線ガード。実射(fireShot)と同一原点(GetLookRay)＋同一狙点方向で、
        //   対象より手前の射線帯に友軍（他プレイヤー＋allyドローン）が居れば、狙点が通っていても発砲しない。
        //   既存の shootable(狙点探索/遮蔽)は「頭が1点通れば撃つ」で緩く、拡散＋原点差で友軍に当たっていた
        //   （FF漏れ実測）。ここで実射ラインを直接検証して塞ぐ。
        private static bool VerifyFriendlyNotInRay()
        {
            if (Cfg.FriendlyFireGate && FriendlyInLineOfFire(self, aimPoint, out int ffBlockerId))
            {
                ReleaseFireIfPressed(self); // [bow] ドロー中ならキャンセル（暴発回避）
                if (Time.time >= _nextHoldLogTime)
                {
                    _nextHoldLogTime = Time.time + Cfg.LogThrottleSec;
                    Log.Out($"[CompanionAI] hold: {threat.Kind} id={tgt.entityId} d={d:0.0}m reason=FF id={ffBlockerId}");
                }
                return;
            }
        }

        // ★ v0.8(D): 友軍射線ガード。
        //   実射 fireShot は GetLookRay().origin(=目, EntityAlive:5536)から狙点方向へ拡散付きで飛ぶ。
        //   ここでは同一原点→aimPoint の直線に対し、対象より手前(dist<狙点距離)で友軍のAABB(膨張)に
        //   交差するものが1体でもあればホールドする。友軍=自分以外の生存プレイヤー＋allyドローン。
        //   膨張量 FriendlyFireMargin は拡散＋コライダー幅ぶんの余裕（片側マージン）。
        private static readonly List<EntityAlive> _ffFriendlies = new List<EntityAlive>();

        private static bool FriendlyInLineOfFire(EntityPlayerLocal self, Vector3 aimPoint, out int blockerId)
        {
            blockerId = -1;
            World world = self.world;
            if (world == null) return false;

            Vector3 origin = self.GetLookRay().origin;          // 実射と同一原点
            Vector3 dir    = aimPoint - origin;
            float   dlen   = dir.magnitude;                     // 対象狙点までの距離（この手前だけ問題）
            if (dlen < 1e-4f) return false;
            Ray shotRay = new Ray(origin, dir / dlen);
            float margin = Cfg.FriendlyFireMargin;

            // --- 友軍集合を集める ---
            _ffFriendlies.Clear();
            var players = world.GetPlayers();                   // リモートのリーダーも含む（FindNearestLeader と同経路）
            if (players != null)
                for (int i = 0; i < players.Count; i++)
                {
                    EntityPlayer p = players[i];
                    if (p != null && p != self && !p.IsDead()) _ffFriendlies.Add(p);
                }
            EntityPlayer selfP = self as EntityPlayer;
            var ents = world.Entities != null ? world.Entities.list : null;
            if (ents != null)
                for (int i = 0; i < ents.Count; i++)
                {
                    // allyドローンのみ友軍に含める（fireShot:1449 と同じ isAlly 判定）
                    if (ents[i] is EntityDrone drone && !drone.IsDead() && drone.isAlly(selfP))
                        _ffFriendlies.Add(drone);
                }

            // --- 射線帯の交差判定 ---
            for (int i = 0; i < _ffFriendlies.Count; i++)
            {
                Bounds b = _ffFriendlies[i].boundingBox;        // world AABB (Entity.boundingBox)
                b.Expand(margin * 2f);                          // Expand は総量増加＝片側 margin
                if (b.IntersectRay(shotRay, out float dist) && dist > 0f && dist < dlen)
                {
                    blockerId = _ffFriendlies[i].entityId;
                    return true;
                }
            }
            return false;
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