using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

// =============================================================================
// Companion AI — locomotion + facing + threat-sensing + ENGAGE(melee+ranged)
// (7DTD 3.1.0)
// -----------------------------------------------------------------------------
// このスライスで追加したもの: 発砲スライス（Section F）。ver0.3(近接)に ranged を追加。
//   (1) 交戦の手前で bFirstPersonView / TPCameraCheck を実ログ出力して確定（ver0.3から）。
//   (2) 近接: アクティブ最近傍脅威が射程内なら press 駆動スイング（ver0.3から）。
//   (3) 遠距離: 銃保持時、頭部狙点へエイム→press/release サイクルで発砲。
//       弾切れはゲート自動リロード任せ、実発砲は Meta 差で検出しログ。
//
// ★ ranged 発火モデルの決定的な差（ver0.3 melee と異なる）:
//   セミオート(BurstRoundCount==1)は bInitialPress（押下の立ち上がりフレーム）でしか
//   発火しない(ItemActionRanged:1121,1160)。press を張り続けても初弾のみ。
//   → 連射には press↔release サイクルが必須。melee の press 張りっぱなしは通用しない。
//
// ★ 弾切れ→自動リロード:
//   空撃ち時 CanReload なら requestReload→GameManager.ItemReloadServer(1244-1246,622)。
//   リロード中は Reloading() が press を吸収(1182)、明けたら再開。
//   → トリガーを引き続けるだけでドライバ側のリロード管理は不要。
//
// ★ netsync: 発砲エフェクト/ダメージは ItemActionEffectsServer(1286) でサーバへ複製。
//   ranged も直接 Attack() で netsync-safe（melee と同じ確定事項）。
//
// ★ 実発砲の検出: 弾はチャンバー基準 holdingItemItemValue.Meta(A4)が ConsumeAmmo(1264)
//   で減る。press 前後の Meta 差で「実際に1発出た」を検出→ログ（swallow を誤カウントしない）。
//
// ★ ヘッドショット狙点: getHeadPosition()(Entity:2642=emodel.GetHeadPosition())。
//   両端の頭で dir = target.getHeadPosition() - self.getHeadPosition() が最精度。
//   （近接は SphereRadius 許容があるので従来どおり胴狙い +0.9m のまま）
//
// ★ bFirstPersonView が「実行時に決まる」ことの接地（監査より重要）:
//   spawn/respawn 時 AfterPlayerRespawn(EPL:3715) → AttachedToEntity==null なら
//   SwitchToPreferredCameraMode(EPL:3645) が走る。そこで
//     CameraRestrictionMode==0 → SetFirstPersonView(bPreferFirstPerson, ...)
//     bPreferFirstPerson は OptionsGfxDefaultFirstPersonCamera(EPL:1282) 由来
//     CameraRestrictionMode!=0 → SetFirstPersonView(num==1, ...)（サーバ強制）
//   ＝ コンパニオンPCのグラフィック設定 or サーバ設定で false になり得る。
//   → デフォルト true(EPL:395) は保証されない。だから実ログで確定させる。
//
// ★ bFirstPersonView==true で攻撃ゲートが全消しになる接地:
//   CharacterCameraAngleValid(EPL:5969): if(bFirstPersonView||Locked3rdPerson) return Pass;
//   canStartAttack(ItemActionDynamicMelee:337): TPCamera分岐は { bFirstPersonView:false } 限定。
//   さらに eTPCameraCheckResult.Pass==0（enum既定値）で二重に安全。
//
// ★ 攻撃レイは camera 由来 → SetRotation で操舵できる:
//   GetLookRay/GetMeleeRay(EPL:3847,3869) は playerCamera から発射。
//   SetRotation(EPL:2310) は m_vp_FPCamera.Angle を更新 → facing用の SetRotation が
//   そのまま攻撃レイを操舵する（facingスライスで視覚確認済みの経路を再利用）。
//
// ★ 実行モデル（ItemActionDynamicMelee）:
//   Attack(false)=press: canStartAttack 通過で Attacking=true＝スイング開始(EAlive:6164→6142)
//   Attack(true)=release: SetAttackFinished（後始末）
//   実ヒットは hold 中に Inventory が holdingItem.OnHoldingUpdate(Inventory:403) を
//   毎フレーム駆動して適用。press を張り続けると canStartAttack の APM 律速
//   (ItemActionDynamicMelee:358) がケイデンスを自動制御 → 多重発火なし。
//   ダメージのレプリケーションは下流 DamageEntity→SendToServer(NetPackageDamageEntity)
//   に内包＝直接 Attack() 呼び出しは netsync-safe（監査確定事項）。
//
// 本スライスの範囲(意図的に絞る):
//   - 近接のみ。Actions[0] is ItemActionRanged は「遠距離＝撃たない」でログのみ。
//   - 脅威への接近(engage maneuver)は未実装。射程内に来た脅威のみ叩く（据え置き）。
//   - 攻撃対象は「アクティブ最近傍脅威」。友軍(リーダー)狙い脅威の拾い上げは別スライス。
//
// 導入: COMPANION クライアントPCにのみ入れる。F8 で駆動ON/OFF。
// 参照DLL: Assembly-CSharp.dll / UnityEngine.CoreModule.dll / 0Harmony.dll
// =============================================================================

namespace CompanionAIVerify
{
    // --- Mod entry -----------------------------------------------------------
    public class CompanionAIVerifyModApi : IModApi
    {
        public void InitMod(Mod _modInstance)
        {
            var harmony = new Harmony("companionai.verify");
            harmony.PatchAll();
            Log.Out("[CompanionAI] verify harness loaded (follow + facing + threat-scan + engage). F8 to toggle drive.");
        }
    }

    // --- Tunables ------------------------------------------------------------
    internal static class Cfg
    {
        internal static bool    Enabled          = false;          // 起動時OFF。F8でトグル
        internal const  KeyCode ToggleKey        = KeyCode.F8;
        internal const  float   StandoffMeters   = 3.0f;           // これ以内なら停止
        internal const  float   RunMeters        = 8.0f;           // これ以上離れたら走る

        internal const  float   ThreatScanRadius = 20.0f;          // 脅威走査半径(m)
        internal static bool    CombatMode       = true;           // true=脅威を向く/叩く
        internal const  float   LogThrottleSec   = 0.5f;           // 検知/交戦ログの最小間隔

        // --- 交戦スライス（近接, ver0.3） ---
        internal const  float   ReachBuffer      = 0.5f;           // 近接射程判定の余裕(m)
        // ★ まずは実ログで bFirstPersonView の実値を「観測」する。
        //   観測して false と分かったら、下を true にして spawn 経路の誤設定を自己修復する。
        internal static bool    ForceFirstPerson = false;

        // --- 発砲スライス（遠距離, ver0.4） ---
        internal static bool    EnableRangedFire     = true;       // false で従来の deferred ログのみ
        internal const  float   RangedMaxEngageMeters= 18.0f;      // これ以内の脅威にのみ発砲(m)
        internal const  float   RangedFireIntervalSec= 0.4f;       // 発砲ケイデンス(≒2.5発/秒)。個々の弾が見える程度に抑制
    }

    // --- Threat sensing ------------------------------------------------------
    internal enum ThreatKind { Zombie, EnemyAnimal, HostileHuman, OtherEnemy, PassiveAnimal, Player, Unknown }
    internal enum Awareness  { Unawakened, Awakening, Engaged }

    internal struct ThreatInfo
    {
        internal EntityAlive Target;
        internal ThreatKind  Kind;
        internal Awareness   State;
        internal float       DistSq;
        internal bool        Valid;
    }

    internal static class ThreatScanner
    {
        internal static int LastHostileCount;
        internal static int LastSleepingCount;

        internal static ThreatInfo ScanNearestActiveThreat(World world, EntityPlayerLocal self)
        {
            ThreatInfo best = default; // Valid=false
            best.DistSq = float.MaxValue;
            LastHostileCount = 0;
            LastSleepingCount = 0;

            float r = Cfg.ThreatScanRadius;
            var box = new Bounds(self.position, new Vector3(r * 2f, r * 2f, r * 2f));

            List<EntityAlive> found = world.GetLivingEntitiesInBounds(self, box);
            if (found == null) return best;

            float rSq = r * r;
            for (int i = 0; i < found.Count; i++)
            {
                EntityAlive e = found[i];
                if (e == null || e == self || e.IsDead()) continue;

                ThreatKind kind = Classify(e);
                if (!IsHostile(kind)) continue;

                Vector3 d = e.position - self.position;
                float dSq = d.sqrMagnitude;
                if (dSq > rSq) continue;

                LastHostileCount++;

                Awareness st = GetAwareness(e, self);
                if (st == Awareness.Unawakened) { LastSleepingCount++; continue; }

                if (dSq < best.DistSq)
                {
                    best.Target = e;
                    best.Kind   = kind;
                    best.State  = st;
                    best.DistSq = dSq;
                    best.Valid  = true;
                }
            }
            return best;
        }

        private static ThreatKind Classify(EntityAlive e)
        {
            switch (e)
            {
                case EntityZombie _:        return ThreatKind.Zombie;
                case EntityEnemyAnimal _:   return ThreatKind.EnemyAnimal;
                case EntityHuman _:
                    return e.EntityClass != null && e.EntityClass.bIsEnemyEntity
                        ? ThreatKind.HostileHuman
                        : ThreatKind.Unknown;
                case EntityEnemy _:         return ThreatKind.OtherEnemy;
                case EntityAnimal _:        return ThreatKind.PassiveAnimal;
                case EntityPlayer _:        return ThreatKind.Player;
                default:                    return ThreatKind.Unknown;
            }
        }

        private static bool IsHostile(ThreatKind k)
        {
            return k == ThreatKind.Zombie
                || k == ThreatKind.EnemyAnimal
                || k == ThreatKind.HostileHuman
                || k == ThreatKind.OtherEnemy;
        }

        private static Awareness GetAwareness(EntityAlive e, EntityPlayerLocal self)
        {
            if (e.IsSleeping) return Awareness.Unawakened;

            EntityAlive tgt = e.GetAttackTargetLocal(); // remote時 attackTargetClient
            if (tgt != null && tgt.entityId == self.entityId)
                return Awareness.Engaged;

            return Awareness.Awakening;
        }
    }

    // --- Combat (engage slice) ----------------------------------------------
    internal static class CombatDriver
    {
        private static bool  _attackPressed;      // 近接: press 中フラグ
        private static float _nextEngageLogTime;
        private static bool  _fpvLogged;
        private static bool  _lastFpv;

        // 遠距離: press/release マイクロサイクルとリロード可視化用
        private static bool  _firePressed;        // 前フレームで press した→今フレーム release
        private static float _nextFireTime;
        private static int   _lastMeta = int.MinValue;

        // 交戦オーバーレイ。posture 決定の後に最後に呼ぶ（in-range 時の 3D エイムが
        // 平面 facing を同フレーム内で上書きするため）。
        internal static void OnCombatStep(EntityPlayerLocal self, in ThreatInfo threat)
        {
            if (!Cfg.CombatMode || !threat.Valid) { ReleaseIfPressed(self); ReleaseFireIfPressed(self); return; }

            float reach = GetAttackReach(self);
            float d     = Mathf.Sqrt(threat.DistSq);
            bool  inRange = d <= reach + Cfg.ReachBuffer;

            bool isRanged;
            bool melee = IsMeleeHolding(self, out isRanged);

            // ★ (1) 交戦の手前で bFirstPersonView を実ログ確定（近接/遠距離とも）
            bool aboutToEngage = isRanged ? (d <= Cfg.RangedMaxEngageMeters) : inRange;
            if (aboutToEngage) ConfirmFirstPersonView(self);

            if (isRanged) // ★ (3) 遠距離: 発砲スライス
            {
                RangedStep(self, in threat, d);
                return;
            }

            if (!inRange) { ReleaseIfPressed(self); return; }

            // ★ (2) 近接交戦: 3Dエイム（ピッチ込み）→ press 駆動スイング
            FaceTarget3D(self, threat.Target);

            if (self.Attack(false)) // press。ケイデンスは canStartAttack の APM 律速が制御
            {
                _attackPressed = true;
                if (Time.time >= _nextEngageLogTime)
                {
                    _nextEngageLogTime = Time.time + Cfg.LogThrottleSec;
                    Log.Out(string.Format(
                        "[CompanionAI] engage: swing {0} {1} d={2:0.0}m reach={3:0.0}m",
                        threat.Kind, threat.State, d, reach));
                }
            }
        }

        internal static void ReleaseIfPressed(EntityPlayerLocal self)
        {
            if (_attackPressed)
            {
                self.Attack(true); // release（スイングの後始末）
                _attackPressed = false;
            }
        }

        // 遠距離のトリガーも安全に開放（脅威消失/無効化/切替時に呼ぶ）。
        internal static void ReleaseFireIfPressed(EntityPlayerLocal self)
        {
            if (_firePressed)
            {
                self.Attack(true);
                _firePressed = false;
            }
        }

        // ★ 発砲ドライバ。press(フレームN)→release(フレームN+1) を FireInterval ごとに回す。
        //   セミオートは press の立ち上がりで1発。オート/バーストも1発ずつの安全ケイデンスに揃う。
        //   弾切れ→自動リロードはゲート任せ。実発砲は Meta 差で検出。
        private static void RangedStep(EntityPlayerLocal self, in ThreatInfo threat, float d)
        {
            if (!Cfg.EnableRangedFire) // 従来どおり撃たずにログのみ
            {
                ReleaseFireIfPressed(self);
                if (d <= Cfg.RangedMaxEngageMeters && Time.time >= _nextEngageLogTime)
                {
                    _nextEngageLogTime = Time.time + 1.0f;
                    Log.Out(string.Format(
                        "[CompanionAI] engage: ranged holding within reach (d={0:0.0}m) — fire disabled.", d));
                }
                return;
            }

            if (d > Cfg.RangedMaxEngageMeters) { ReleaseFireIfPressed(self); return; }

            // 頭部狙点へエイム（両端の頭ボーンで最精度）
            FaceTargetHead(self, threat.Target);

            // release フェーズ優先：前フレームで press 済みなら今フレームは離す
            if (_firePressed)
            {
                self.Attack(true);
                _firePressed = false;
                return;
            }

            // press フェーズ：ケイデンス到来時のみ
            if (Time.time < _nextFireTime) return;

            int before = GetHoldingMeta(self);
            self.Attack(false);                 // press = 発火(セミ)/開始(オート)
            _firePressed = true;                // 次フレームで必ず release（内部ゲートに関わらず整定）
            _nextFireTime = Time.time + Cfg.RangedFireIntervalSec;

            int after = GetHoldingMeta(self);
            if (after >= 0 && before >= 0)
            {
                if (after < before) // 実際に1発消費された＝発砲成立
                {
                    Log.Out(string.Format(
                        "[CompanionAI] fire: {0} {1} d={2:0.0}m mag={3}",
                        threat.Kind, threat.State, d, after));
                }
                else if (after == 0) // 空＝リロード待ち（ゲートが自動要求）
                {
                    if (_lastMeta != 0)
                        Log.Out("[CompanionAI] fire: empty — waiting for auto-reload.");
                }
                else if (_lastMeta >= 0 && after > _lastMeta) // Meta 増加＝リロード完了
                {
                    Log.Out(string.Format("[CompanionAI] reload: done, mag={0}", after));
                }
                _lastMeta = after;
            }
        }

        // 保持中アイテムの装填残弾(Meta)。取得不可は -1。 (A4: holdingItemItemValue.Meta)
        private static int GetHoldingMeta(EntityPlayerLocal self)
        {
            var inv = self.inventory;
            var iv  = inv != null ? inv.holdingItemItemValue : null;
            return iv != null ? iv.Meta : -1;
        }

        // 頭部→頭部でエイム。ranged ショットは GetLookRay(camera) 由来なので
        // SetRotation でカメラを向ければ着弾する（ItemActionRanged:1579, EPL:2310）。
        private static void FaceTargetHead(EntityPlayerLocal self, EntityAlive target)
        {
            Vector3 eye  = self.getHeadPosition();
            Vector3 head = target.getHeadPosition();
            Vector3 dir  = head - eye;
            if (dir.sqrMagnitude < 1e-6f) return;

            Vector3 euler = Quaternion.LookRotation(dir.normalized, Vector3.up).eulerAngles;
            euler.x *= -1f; // ピッチ反転（EPL:239, 251）
            self.SetRotation(euler);
        }

        // 保持アイテム action[0] の射程。取れなければ素手相当 2.0m。
        private static float GetAttackReach(EntityPlayerLocal self)
        {
            var hi = self.inventory != null ? self.inventory.holdingItem : null;
            var a  = (hi != null && hi.Actions != null && hi.Actions.Length > 0) ? hi.Actions[0] : null;
            if (a != null && a.Range > 0.01f) return a.Range;
            return 2.0f;
        }

        // 近接か遠距離か。ItemActionRanged のみ遠距離扱い、それ以外(素手/工具/近接武器)は近接。
        private static bool IsMeleeHolding(EntityPlayerLocal self, out bool isRanged)
        {
            var hi = self.inventory != null ? self.inventory.holdingItem : null;
            var a  = (hi != null && hi.Actions != null && hi.Actions.Length > 0) ? hi.Actions[0] : null;
            isRanged = a is ItemActionRanged;
            return !isRanged;
        }

        // 交戦の手前で bFirstPersonView を実ログ。初回 or 変化時のみ出力。
        private static void ConfirmFirstPersonView(EntityPlayerLocal self)
        {
            bool fpv = self.bFirstPersonView;
            if (_fpvLogged && fpv == _lastFpv) return;

            _fpvLogged = true;
            _lastFpv   = fpv;
            Log.Out(string.Format(
                "[CompanionAI] engage-precheck: bFirstPersonView={0} TPCam={1} camPassed={2}",
                fpv, self.TPCameraCheckResult, self.TPCameraCheckPassed));

            if (!fpv && Cfg.ForceFirstPerson)
            {
                self.SetFirstPersonView(true, false); // spawn経路の誤設定を自己修復
                Log.Out("[CompanionAI] engage-precheck: forced bFirstPersonView=true (ForceFirstPerson).");
            }
        }

        // ピッチ込みで対象中心付近を狙う（低い脅威にも当てるため y を潰さない）。
        // 変換式は facing と同一（EPL:2310, 248-252）。camera 経由で攻撃レイが操舵される。
        private static void FaceTarget3D(EntityPlayerLocal self, EntityAlive target)
        {
            Vector3 eye = self.position + Vector3.up * 1.5f;   // 概算カメラ高
            Vector3 aim = target.position + Vector3.up * 0.9f; // 概算胴中心
            Vector3 dir = aim - eye;
            if (dir.sqrMagnitude < 1e-6f) return;

            Vector3 euler = Quaternion.LookRotation(dir.normalized, Vector3.up).eulerAngles;
            euler.x *= -1f; // ピッチ反転（EPL:239, 251）
            self.SetRotation(euler);
        }
    }

    // --- Executor ------------------------------------------------------------
    internal static class CompanionExecutor
    {
        private static int   _lastLoggedThreatId = int.MinValue;
        private static float _nextLogTime;

        internal static void OnMovePrefix(EntityPlayerLocal self)
        {
            if (Input.GetKeyDown(Cfg.ToggleKey))
            {
                Cfg.Enabled = !Cfg.Enabled;
                Log.Out("[CompanionAI] drive = " + Cfg.Enabled);
                if (!Cfg.Enabled) { CombatDriver.ReleaseIfPressed(self); CombatDriver.ReleaseFireIfPressed(self); Stop(self); }
            }
            if (!Cfg.Enabled) return;

            var world = GameManager.Instance != null ? GameManager.Instance.World : null;
            if (world == null || self != world.GetPrimaryPlayer()) return;

            EntityPlayer leader = FindNearestLeader(world, self);
            if (leader == null) { CombatDriver.ReleaseIfPressed(self); CombatDriver.ReleaseFireIfPressed(self); Stop(self); return; }

            // --- 脅威検知（Section B） ---
            ThreatInfo threat = ThreatScanner.ScanNearestActiveThreat(world, self);
            LogThreat(threat);

            // --- posture: follow ---
            Vector3 flat = leader.position - self.position; flat.y = 0f;
            float dist = flat.magnitude;

            Vector3 lookDir = flat;
            if (Cfg.CombatMode && threat.Valid)
            {
                Vector3 toThreat = threat.Target.position - self.position; toThreat.y = 0f;
                if (toThreat.sqrMagnitude > 0.001f) lookDir = toThreat;
            }

            if (dist <= Cfg.StandoffMeters)
            {
                if (Cfg.CombatMode && threat.Valid) FaceOnly(self, lookDir);
                Stop(self);
            }
            else
            {
                Steer(self, moveTarget: leader.position, lookDir: lookDir, running: dist > Cfg.RunMeters);
            }

            // --- 交戦オーバーレイ（Section E）: 最後に実行 ---
            //   in-range の近接は 3D エイムで上の平面 facing を上書きしつつ press 駆動。
            CombatDriver.OnCombatStep(self, threat);
        }

        private static void Steer(EntityPlayerLocal self, Vector3 moveTarget, Vector3 lookDir, bool running)
        {
            Vector3 toTarget = moveTarget - self.position; toTarget.y = 0f;
            if (toTarget.sqrMagnitude < 0.01f) { Stop(self); return; }
            Vector3 moveWorld = toTarget.normalized;

            Vector3 ld = lookDir; ld.y = 0f;
            Vector3 lookFwd;
            if (ld.sqrMagnitude > 0.001f)
            {
                lookFwd = ld.normalized;
                FaceWorldDir(self, lookFwd);
            }
            else
            {
                lookFwd = moveWorld;
            }

            Vector3 lookRight = Vector3.Cross(Vector3.up, lookFwd);
            self.movementInput.moveForward = Mathf.Clamp(Vector3.Dot(moveWorld, lookFwd),  -1f, 1f);
            self.movementInput.moveStrafe  = Mathf.Clamp(Vector3.Dot(moveWorld, lookRight), -1f, 1f);
            self.movementInput.running     = running;
            self.movementInput.jump        = false;
            self.movementInput.down        = false;
        }

        private static void FaceOnly(EntityPlayerLocal self, Vector3 lookDir)
        {
            Vector3 ld = lookDir; ld.y = 0f;
            if (ld.sqrMagnitude > 0.001f) FaceWorldDir(self, ld.normalized);
        }

        private static void FaceWorldDir(EntityPlayerLocal self, Vector3 worldDir)
        {
            if (worldDir.sqrMagnitude < 1e-6f) return;
            Vector3 euler = Quaternion.LookRotation(worldDir.normalized, Vector3.up).eulerAngles;
            euler.x *= -1f;
            self.SetRotation(euler);
        }

        private static void Stop(EntityPlayerLocal self)
        {
            self.movementInput.moveForward = 0f;
            self.movementInput.moveStrafe  = 0f;
            self.movementInput.running     = false;
            self.movementInput.jump        = false;
            self.movementInput.down        = false;
        }

        private static EntityPlayer FindNearestLeader(World world, EntityPlayerLocal self)
        {
            var players = world.GetPlayers();
            if (players == null) return null;
            EntityPlayer best = null;
            float bestSq = float.MaxValue;
            for (int i = 0; i < players.Count; i++)
            {
                EntityPlayer p = players[i];
                if (p == null || p == self || p.IsDead()) continue;
                float sq = (p.position - self.position).sqrMagnitude;
                if (sq < bestSq) { bestSq = sq; best = p; }
            }
            return best;
        }

        private static void LogThreat(ThreatInfo t)
        {
            int id = t.Valid ? t.Target.entityId : int.MinValue;
            bool changed = id != _lastLoggedThreatId;
            if (!changed && Time.time < _nextLogTime) return;

            _lastLoggedThreatId = id;
            _nextLogTime = Time.time + Cfg.LogThrottleSec;

            if (t.Valid)
            {
                float d = Mathf.Sqrt(t.DistSq);
                Log.Out(string.Format(
                    "[CompanionAI] threat: {0} {1} d={2:0.0}m (hostiles={3}, sleeping={4})",
                    t.Kind, t.State, d, ThreatScanner.LastHostileCount, ThreatScanner.LastSleepingCount));
            }
            else
            {
                Log.Out(string.Format(
                    "[CompanionAI] threat: none (hostiles={0}, sleeping={1})",
                    ThreatScanner.LastHostileCount, ThreatScanner.LastSleepingCount));
            }
        }
    }

    // --- Harmony patch -------------------------------------------------------
    [HarmonyPatch(typeof(EntityPlayerLocal), "MoveByInput")]
    internal static class Patch_EntityPlayerLocal_MoveByInput
    {
        private static void Prefix(EntityPlayerLocal __instance)
        {
            CompanionExecutor.OnMovePrefix(__instance);
        }
    }
}
