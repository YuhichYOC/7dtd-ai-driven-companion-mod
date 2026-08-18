using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

// =============================================================================
// Companion AI — locomotion + facing + threat-sensing verification harness
// (7DTD 3.1.0)
// -----------------------------------------------------------------------------
// このスライスで追加したもの: 脅威検知（Section B）。
//   近傍 EntityAlive を列挙 → 種別分類 → 三値覚醒状態 → 「アクティブ最近傍脅威」特定。
//   可視確認のため、アクティブ脅威がいれば follow の lookDir をそちらへ差し替え、
//   検知内容を throttle ログへ出す。
//
// ★クライアント側で確定した重要事実（監査 B5 の訂正）:
//   敵はコンパニオン(クライアント)から見て全て「リモート」。攻撃対象は
//     - サーバ側フィールド attackTarget はネットで埋まらない
//     - NetPackageSetAttackTarget → SetAttackTargetClient → attackTargetClient が埋まる
//   よって攻撃対象は GetAttackTargetLocal()（remote時 attackTargetClient を返す）で読む。
//   GetAttackTarget()/attackTarget 直読は不可。 (EntityAlive:5900-5907, 5930-5941)
//
//   IsSleeping は別扱いで安全: 起床時にサーバが NetPackageSleeperWakeup を送り、
//   同一フィールド IsSleeping が更新される（client 専用フィールドなし）→ 直読可。
//     (EntityAlive:440, 2651-2654)
//
// 三値覚醒マッピング（クライアント接地版, 監査C章）:
//   未覚醒 : IsSleeping == true
//   交戦中 : GetAttackTargetLocal() == ローカルプレイヤー   ← 友軍判定は据え置き
//   覚醒中 : IsSleeping == false ∧ 上記でない
//   「アクティブ脅威」= 敵対 ∧ IsSleeping == false（＝覚醒中 or 交戦中）。未覚醒は向かない。
//
// 分類（最派生先行スイッチ, 監査B3）:
//   EntityZombie → EntityEnemyAnimal → EntityHuman(非ゾンビ) → EntityEnemy → EntityAnimal → EntityPlayer
//   動物は型で敵対性確定。非ゾンビ人間だけ EntityClass.bIsEnemyEntity で敵対性を確認
//   （EntityClass getter は辞書引きなので、この分岐でのみ参照）(Entity:621, EntityAlive:4993)
//
// 列挙: World.GetLivingEntitiesInBounds(EntityAlive 除外, Bounds) は
//   再利用バッファ(毎回Clear)へ生存 EntityAlive のみ格納し自機を除外 → 毎フレーム走査でGC無。
//   返り値は共有バッファのため、同フレーム内で即消費し保持しない。 (World:2358-2373)
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
            Log.Out("[CompanionAI] verify harness loaded (follow + facing=SetRotation + threat-scan). F8 to toggle drive.");
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
        // モード宣言の縫い目（本番はリーダー宣言で切替）。true=交戦モード相当（脅威を向く）。
        // 移動モード配線は後段。false にすると脅威がいても進行方向を向く。
        internal static bool    CombatMode       = true;
        internal const  float   LogThrottleSec   = 0.5f;           // 検知ログの最小間隔
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
        // 走査結果の要約（ログ用）
        internal static int LastHostileCount;
        internal static int LastSleepingCount;

        // 近傍の「アクティブ最近傍脅威」を返す。無ければ Valid=false。
        // 副産物として敵対数/睡眠数を集計（検証ログ用）。
        internal static ThreatInfo ScanNearestActiveThreat(World world, EntityPlayerLocal self)
        {
            ThreatInfo best = default; // Valid=false
            best.DistSq = float.MaxValue;
            LastHostileCount = 0;
            LastSleepingCount = 0;

            float r = Cfg.ThreatScanRadius;
            var box = new Bounds(self.position, new Vector3(r * 2f, r * 2f, r * 2f));

            // 共有バッファ。同フレーム内で即消費する（保持しない）。
            List<EntityAlive> found = world.GetLivingEntitiesInBounds(self, box);
            if (found == null) return best;

            float rSq = r * r;
            for (int i = 0; i < found.Count; i++)
            {
                EntityAlive e = found[i];
                if (e == null || e == self || e.IsDead()) continue;

                ThreatKind kind = Classify(e);
                if (!IsHostile(kind)) continue; // プレイヤー・非敵対動物は脅威でない

                Vector3 d = e.position - self.position;
                float dSq = d.sqrMagnitude;
                if (dSq > rSq) continue; // 箱の角を落として球で絞る

                LastHostileCount++;

                Awareness st = GetAwareness(e, self);
                if (st == Awareness.Unawakened) { LastSleepingCount++; continue; } // 未覚醒は向かない

                // アクティブ脅威候補: 最近傍を採用
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

        // 静的kind軸（最派生先行）。動的敵対性は GetAwareness が担当。
        private static ThreatKind Classify(EntityAlive e)
        {
            switch (e)
            {
                case EntityZombie _:        return ThreatKind.Zombie;         // 常に敵対
                case EntityEnemyAnimal _:   return ThreatKind.EnemyAnimal;    // 型で敵対確定
                case EntityHuman _:         // ゾンビは上で除外済 → 生身の人間
                    return e.EntityClass != null && e.EntityClass.bIsEnemyEntity
                        ? ThreatKind.HostileHuman
                        : ThreatKind.Unknown; // 非敵対人間(NPC等)は脅威扱いしない
                case EntityEnemy _:         return ThreatKind.OtherEnemy;     // 将来クラスの保険
                case EntityAnimal _:        return ThreatKind.PassiveAnimal;  // 非敵対
                case EntityPlayer _:        return ThreatKind.Player;         // リーダー/友軍/他人
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

        // 三値覚醒（クライアント接地）: 攻撃対象は GetAttackTargetLocal() で読む。
        private static Awareness GetAwareness(EntityAlive e, EntityPlayerLocal self)
        {
            if (e.IsSleeping) return Awareness.Unawakened;

            EntityAlive tgt = e.GetAttackTargetLocal(); // remote時 attackTargetClient
            if (tgt != null && tgt.entityId == self.entityId)
                return Awareness.Engaged; // ローカルプレイヤーを狙っている（友軍判定は据え置き）

            return Awareness.Awakening;
        }
    }

    // --- Executor ------------------------------------------------------------
    internal static class CompanionExecutor
    {
        // 検知ログの変化検出用
        private static int   _lastLoggedThreatId = int.MinValue;
        private static float _nextLogTime;

        internal static void OnMovePrefix(EntityPlayerLocal self)
        {
            if (Input.GetKeyDown(Cfg.ToggleKey))
            {
                Cfg.Enabled = !Cfg.Enabled;
                Log.Out("[CompanionAI] drive = " + Cfg.Enabled);
                if (!Cfg.Enabled) Stop(self);
            }
            if (!Cfg.Enabled) return;

            var world = GameManager.Instance != null ? GameManager.Instance.World : null;
            if (world == null || self != world.GetPrimaryPlayer()) return;

            EntityPlayer leader = FindNearestLeader(world, self);
            if (leader == null) { Stop(self); return; }

            // --- 脅威検知（Section B） ---
            ThreatInfo threat = ThreatScanner.ScanNearestActiveThreat(world, self);
            LogThreat(threat);

            // --- posture: follow ---
            Vector3 flat = leader.position - self.position; flat.y = 0f;
            float dist = flat.magnitude;

            // 体の向き: 交戦モード ∧ アクティブ脅威あり → 脅威を向く。さもなくば進行方向。
            //   （未覚醒脅威は Scan 側で除外済 = 向かない = 設計どおり）
            Vector3 lookDir = flat;
            if (Cfg.CombatMode && threat.Valid)
            {
                Vector3 toThreat = threat.Target.position - self.position; toThreat.y = 0f;
                if (toThreat.sqrMagnitude > 0.001f) lookDir = toThreat;
            }

            if (dist <= Cfg.StandoffMeters)
            {
                // 追従は停止。ただし脅威がいれば体だけ向ける（stationary facing）。
                if (Cfg.CombatMode && threat.Valid) FaceOnly(self, lookDir);
                Stop(self);
                return;
            }

            Steer(self, moveTarget: leader.position, lookDir: lookDir, running: dist > Cfg.RunMeters);
        }

        // R_steer: move(どこへ) と look(どこを向く) を独立入力に。
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
                FaceWorldDir(self, lookFwd);          // SetRotation 経路
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

        // 停止中に体だけ向ける（移動入力は変えない）。
        private static void FaceOnly(EntityPlayerLocal self, Vector3 lookDir)
        {
            Vector3 ld = lookDir; ld.y = 0f;
            if (ld.sqrMagnitude > 0.001f) FaceWorldDir(self, ld.normalized);
        }

        // facing（検証パターン(1): SetRotation 経路）。game 本体の look-at と同一変換。
        //   (EntityPlayerLocal:2310-2321, Entity:2581-2585 / 変換式 EPL:248-252)
        //   パターン(2)へ切替時はこの関数の中身のみ差し替え。
        private static void FaceWorldDir(EntityPlayerLocal self, Vector3 worldDir)
        {
            if (worldDir.sqrMagnitude < 1e-6f) return;
            Vector3 euler = Quaternion.LookRotation(worldDir.normalized, Vector3.up).eulerAngles;
            euler.x *= -1f;                            // ピッチ反転（EPL:239, 251）
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

        // 検証ログ: 対象が変わった時、または throttle 間隔ごとに要約を出す。
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
