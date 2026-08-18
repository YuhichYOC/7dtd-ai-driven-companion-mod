using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

// =============================================================================
// Companion AI verify harness — Build v0.6.0 (hit検証+シュータブル・ゲート / フルオート連射)
//   採番方針: 交戦(engage)スライス系列は v0.5.x。tuning/診断/tooling は patch(.1,.2,.3,.4)、
//   新capability(engage-maneuver/navigation 等)が入る時のみ minor を上げる。
//   v0.6.0: 発砲前に Voxel.Raycast で射線検証（遮蔽/FF/狙点外しを撃たずホールド, 候補狙点探索）
//          ＋ フルオート武器(GetBurstCount==0)を press 保持で RPM 連射。
// -----------------------------------------------------------------------------
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
// ── ver0.4.1 追加(計測): fire ログに intended vs actual-hit を記録 ──
//   狙ったターゲット(intended) = threat.Target（我々の選択）。
//   実際に当てたもの(actual)   = self.MinEventContext.Other（fireShot がゲート内で設定,
//     命中エンティティ / 非命中は null。ItemActionRanged:1194 null化, 1462 格納）。
//   → hit=TARGET / OTHER id=N(FF疑い) / none(block/miss) を判別。
//
// ── ver0.5 追加(修正): ハイブリッド狙点 ──
//   計測結果: 命中は headLift≈1.5-2.0、非命中は≈0-0.7（負値は頭が足元より下）。
//   頭ボーンは低姿勢(四足/突進/のけぞり)で当たり判定を外すため、headLift でゲート:
//     headLift >= HeadAimMinLift → 頭狙い（立ち姿勢のヘッドショット維持）
//     未満                        → position + up*scaledExtent.y（AABB縦中心, 姿勢非依存）
//   fire ログに aim=head/center と aimLift を追加し、低headLift の弾が
//   none→TARGET に転じるかをテストで直接集計できるようにする（しきい値は要調整）。
//
// ── ver0.6 追加(診断+修正候補): カメラ配達ラグ ──
//   観察: body(見た目)は標的を向くのに弾が上へ抜ける／シングルなら当たる。
//   原因仮説: 弾は GetLookRay()=playerCamera.transform 由来だが、SetRotation は
//     カメラを m_vp_FPCamera.Angle 経由で遅延反映(vp_FPCamera更新はLateUpdate付近)。
//     同フレーム発砲では前フレームのカメラ向きで撃つ→急ピッチの低標的で上に外す。
//   診断: fire ログに errDeg(実レイ GetLookRay vs 意図方向) と pWant/pAct(ピッチ) を追加。
//   修正候補(トグル SnapCameraOnFire): 発砲直前に playerCamera.transform を狙点へ即時スナップ。
//     false=ベースライン(errDeg 大を確認) / true=errDeg≈0 と命中改善を確認（同一セッションA/B）。
//
// ── ver0.7 追加(修正): 視差(パララックス)補正 ──
//   実測: snap=true で errDeg=0（配達ラグ解消）だが、至近＋大俯角のみ none 連発。
//   原因: 狙い方向を頭ボーン基準で作っていたが、弾は GetLookRay()=カメラ位置から出る。
//     頭とカメラの微小オフセットは遠距離で無視できても至近で致命的（距離依存の外れ）。
//   修正: スナップ方向を「カメラ実ワールド位置(playerCamera.transform.position+Origin.position)
//     → 狙点」で計算。Entity.position はワールド(Entity:938)。既定 SnapCameraOnFire=true。
//   診断: missDist（実レイ GetLookRay と aimPoint の最短距離）を追加。至近で≈0 に落ちれば視差確定。
//
// ── ver0.8 追加(改善): ADS（サイトを覗く射撃） ──
//   これまで全弾ヒップ＝最大拡散。AimingGun=true で拡散 hip(1.0)→aiming(0.1) の10倍縮小
//   (ItemActionRanged:1346, 更新は 748 で holdingEntity.AimingGun を参照)。
//   視差(v0.7)とは別軸で、狙点周りの散布界を絞る。発砲前に SetAds(true)、
//   離脱時 ReleaseFireIfPressed で SetAds(false)。secondary action(Actions[1]) 持ちのみ。
//   fire ログに ads=on/off を追加。
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
            ModCfgFile.Init(_modInstance);   // companion_config.txt を読込（無ければ生成）
            Log.Out("[CompanionAI] verify harness v0.6.0 loaded (engage[melee+ranged/parallax/ADS/shootable-gate/full-auto] + file-config). F8 to toggle drive / reload config.");
        }
    }

    // --- Tunables (companion_config.txt で上書き可能) --------------------------
    internal static class Cfg
    {
        internal static bool    Enabled          = false;          // 起動時OFF。F8でトグル(ファイル対象外)
        internal const  KeyCode ToggleKey        = KeyCode.F8;
        internal static float   StandoffMeters   = 3.0f;           // これ以内なら停止
        internal static float   RunMeters        = 8.0f;           // これ以上離れたら走る

        internal static float   ThreatScanRadius = 20.0f;          // 脅威走査半径(m)
        internal static bool    CombatMode       = true;           // true=脅威を向く/叩く
        internal static float   LogThrottleSec   = 0.5f;           // 検知/交戦ログの最小間隔

        // --- 交戦スライス（近接, ver0.3） ---
        internal static float   ReachBuffer      = 0.5f;           // 近接射程判定の余裕(m)
        // ★ 実ログで bFirstPersonView の実値を観測。false と分かれば true で spawn 誤設定を自己修復。
        internal static bool    ForceFirstPerson = false;

        // --- 発砲スライス（遠距離, ver0.4） ---
        internal static bool    EnableRangedFire     = true;       // false で従来の deferred ログのみ
        internal static float   RangedMaxEngageMeters= 18.0f;      // これ以内の脅威にのみ発砲(m)
        internal static float   RangedFireIntervalSec= 0.4f;       // 発砲ケイデンス(≒2.5発/秒)

        // --- ハイブリッド狙点（ver0.5） ---
        //   実測: 命中は headLift≈1.5-2.0、非命中は≈0-0.7 で分離。
        internal static float   HeadAimMinLift       = 1.2f;       // これ以上なら頭狙い、未満は胴中心

        // --- カメラ配達ラグ対策（ver0.6）＋視差補正（ver0.7） ---
        //   発砲直前に playerCamera.transform を狙点へ即時スナップし、配達ラグ＋視差を解消。
        internal static bool    SnapCameraOnFire     = true;

        // --- 視差A/B用トグル（v0.5.3） ---
        //   true=カメラ実位置基準（補正あり） / false=頭ボーン基準（補正なし・旧挙動）。
        internal static bool    AimFromCameraOrigin  = true;

        // --- ADS（サイトを覗く射撃, ver0.8） ---
        //   AimingGun=true で拡散が hip(1.0)→aiming(0.1) と10倍縮む(ItemActionRanged:1346, 748)。
        internal static bool    AimDownSightsOnEngage = true;

        // --- hit検証＋シュータブル・ゲート（v0.6.0） ---
        //   発砲前に自前 Voxel.Raycast で「射線が対象コライダーに届くか」を検証。
        //   候補狙点(頭/胴中心/腹)を順に試し、対象に当たる点だけ採用。全滅なら撃たずホールド。
        //   遮蔽(block)・FF(別entity)・空(sky) を理由としてログ化。
        internal static bool    RequireShootable     = true;

        // --- フルオート連射（v0.6.0） ---
        //   GetBurstCount==0 の武器はトリガー押しっぱなしで RPM 連射（false で全銃 FireInterval 単発）。
        internal static bool    FullAutoHold         = true;
    }

    // --- 外部設定ファイル (companion_config.txt) -------------------------------
    //   Mod フォルダの key=value テキストを起動時＆F8切替時に読込。無ければ既定でテンプレ生成。
    //   bool: true/false/1/0/on/off、float: '.' 区切り(InvariantCulture)。未知キーは警告して無視。
    internal static class ModCfgFile
    {
        private static string _path;

        internal static void Init(Mod mod)
        {
            try
            {
                string dir = (mod != null) ? mod.Path : null;   // 3.1.0 で異なる場合は要確認
                if (string.IsNullOrEmpty(dir))
                {
                    Log.Warning("[CompanionAI] mod path unknown; config disabled, using defaults.");
                    return;
                }
                _path = System.IO.Path.Combine(dir, "companion_config.txt");
                if (!System.IO.File.Exists(_path))
                {
                    try
                    {
                        System.IO.File.WriteAllText(_path, DefaultText());
                        Log.Out("[CompanionAI] wrote default config: " + _path);
                    }
                    catch (System.Exception e)
                    {
                        Log.Warning("[CompanionAI] could not write default config: " + e.Message);
                    }
                }
                Load();
            }
            catch (System.Exception e)
            {
                Log.Warning("[CompanionAI] config init failed: " + e.Message + " (using defaults)");
            }
        }

        internal static void Reload()
        {
            if (string.IsNullOrEmpty(_path)) return;
            try { Load(); }
            catch (System.Exception e) { Log.Warning("[CompanionAI] config reload failed: " + e.Message); }
        }

        private static void Load()
        {
            if (_path == null || !System.IO.File.Exists(_path))
            {
                Log.Out("[CompanionAI] config not found, using defaults.");
                return;
            }
            int applied = 0;
            foreach (string raw in System.IO.File.ReadAllLines(_path))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                string key = line.Substring(0, eq).Trim();
                string val = line.Substring(eq + 1).Trim();
                if (Apply(key, val)) applied++;
                else Log.Warning("[CompanionAI] config: unknown/invalid '" + key + "' = '" + val + "'");
            }
            Log.Out(string.Format(
                "[CompanionAI] config loaded ({0} keys): Combat={1} Ranged={2} Snap={3} AimFromCam={4} ADS={5} ForceFPV={6} | Standoff={7} Run={8} ScanR={9} HeadLift={10} MaxEngage={11} FireInt={12} ReachBuf={13} LogThr={14}",
                applied, Cfg.CombatMode, Cfg.EnableRangedFire, Cfg.SnapCameraOnFire, Cfg.AimFromCameraOrigin,
                Cfg.AimDownSightsOnEngage, Cfg.ForceFirstPerson, Cfg.StandoffMeters, Cfg.RunMeters,
                Cfg.ThreatScanRadius, Cfg.HeadAimMinLift, Cfg.RangedMaxEngageMeters, Cfg.RangedFireIntervalSec,
                Cfg.ReachBuffer, Cfg.LogThrottleSec));
        }

        private static bool Apply(string key, string val)
        {
            switch (key)
            {
                case "CombatMode":            return TryBool(val, ref Cfg.CombatMode);
                case "EnableRangedFire":      return TryBool(val, ref Cfg.EnableRangedFire);
                case "ForceFirstPerson":      return TryBool(val, ref Cfg.ForceFirstPerson);
                case "SnapCameraOnFire":      return TryBool(val, ref Cfg.SnapCameraOnFire);
                case "AimFromCameraOrigin":   return TryBool(val, ref Cfg.AimFromCameraOrigin);
                case "AimDownSightsOnEngage": return TryBool(val, ref Cfg.AimDownSightsOnEngage);
                case "RequireShootable":      return TryBool(val, ref Cfg.RequireShootable);
                case "FullAutoHold":          return TryBool(val, ref Cfg.FullAutoHold);
                case "StandoffMeters":        return TryF(val, ref Cfg.StandoffMeters);
                case "RunMeters":             return TryF(val, ref Cfg.RunMeters);
                case "ThreatScanRadius":      return TryF(val, ref Cfg.ThreatScanRadius);
                case "LogThrottleSec":        return TryF(val, ref Cfg.LogThrottleSec);
                case "ReachBuffer":           return TryF(val, ref Cfg.ReachBuffer);
                case "HeadAimMinLift":        return TryF(val, ref Cfg.HeadAimMinLift);
                case "RangedMaxEngageMeters": return TryF(val, ref Cfg.RangedMaxEngageMeters);
                case "RangedFireIntervalSec": return TryF(val, ref Cfg.RangedFireIntervalSec);
                default: return false;
            }
        }

        private static bool TryBool(string s, ref bool dst)
        {
            switch (s.ToLowerInvariant())
            {
                case "true": case "1": case "on":  case "yes": dst = true;  return true;
                case "false":case "0": case "off": case "no":  dst = false; return true;
                default: return false;
            }
        }

        private static bool TryF(string s, ref float dst)
        {
            if (float.TryParse(s, System.Globalization.NumberStyles.Float,
                               System.Globalization.CultureInfo.InvariantCulture, out float v))
            { dst = v; return true; }
            return false;
        }

        private static string DefaultText()
        {
            return
"# CompanionAI verify harness config (v0.6.0)\n" +
"# 変更後、ゲーム内で F8（ドライブ切替）を押すと再読込されます。起動時にも読込。\n" +
"# bool = true/false (1/0/on/off も可) , float = 小数点は '.'（例 3.0）\n" +
"\n" +
"# --- 交戦の基本 ---\n" +
"CombatMode            = true\n" +
"EnableRangedFire      = true\n" +
"\n" +
"# --- 追従 ---\n" +
"StandoffMeters        = 3.0\n" +
"RunMeters             = 8.0\n" +
"\n" +
"# --- 脅威検知 ---\n" +
"ThreatScanRadius      = 20.0\n" +
"LogThrottleSec        = 0.5\n" +
"\n" +
"# --- 近接 ---\n" +
"ReachBuffer           = 0.5\n" +
"\n" +
"# --- 狙点（頭/胴の切替しきい値, m）---\n" +
"HeadAimMinLift        = 1.2\n" +
"\n" +
"# --- 発砲 ---\n" +
"RangedMaxEngageMeters = 18.0\n" +
"RangedFireIntervalSec = 0.4\n" +
"\n" +
"# --- カメラ/視差/ADS（A/B対象）---\n" +
"ForceFirstPerson      = false\n" +
"SnapCameraOnFire      = true\n" +
"AimFromCameraOrigin   = true\n" +
"AimDownSightsOnEngage = true\n" +
"\n" +
"# --- hit検証ゲート / フルオート（v0.6.0）---\n" +
"RequireShootable      = true\n" +
"FullAutoHold          = true\n";
        }
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
        private static bool  _adsOn;              // ADS(サイト覗き)状態。変化時のみトグル
        private static float _nextHoldLogTime;    // ホールド理由ログの throttle
        // シュータブル候補狙点の再利用バッファ（毎フレーム alloc 回避）
        private static readonly Vector3[] _candPts = new Vector3[3];
        private static readonly string[]  _candNm  = new string[3];

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

        // 遠距離のトリガーも安全に開放（脅威消失/無効化/切替時に呼ぶ）。ADSも解除。
        internal static void ReleaseFireIfPressed(EntityPlayerLocal self)
        {
            if (_firePressed)
            {
                self.Attack(true);
                _firePressed = false;
            }
            SetAds(self, false);
        }

        // ADS(サイト覗き)状態を変化時のみ切替。AimingGun setter は FOV/animator/Actions[1] に
        // 副作用があるため冪等呼び出しを避ける。secondary action を持たない銃では発動しない。
        private static void SetAds(EntityPlayerLocal self, bool on)
        {
            if (on && !Cfg.AimDownSightsOnEngage) on = false;
            if (on && !CanAimDownSights(self)) on = false;
            if (on == _adsOn) return;
            _adsOn = on;
            self.AimingGun = on; // 拡散 hip(1.0)↔aiming(0.1) を切替(ItemActionRanged:748,1346)
        }

        // ADS 可否: secondary action(Actions[1]) と actionData[1] が存在すること。
        // (AimingGun setter が actionData[1] を直接参照＝境界外で例外になるためのガード)
        private static bool CanAimDownSights(EntityPlayerLocal self)
        {
            var inv = self.inventory;
            var hi  = inv != null ? inv.holdingItem : null;
            var hid = inv != null ? inv.holdingItemData : null;
            return hi != null && hi.Actions != null && hi.Actions.Length >= 2 && hi.Actions[1] != null
                && hid != null && hid.actionData != null && hid.actionData.Count >= 2;
        }

        // ★ 発砲ドライバ（v0.6.0）: hit検証＋シュータブル・ゲート＋フルオート連射。
        //   1) 射線が対象コライダーに届く狙点を探す（ResolveShootableAim）。届かなければホールド。
        //   2) 届く狙点へカメラをスナップ（視差/ラグ解消）＋ADS。
        //   3) フルオート(GetBurstCount==0)は press 保持で RPM 連射、セミ/バーストは press/release 単発。
        private static void RangedStep(EntityPlayerLocal self, in ThreatInfo threat, float d)
        {
            if (!Cfg.EnableRangedFire)
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

            EntityAlive tgt = threat.Target;
            float headLift = tgt.getHeadPosition().y - tgt.position.y;

            // カメラ実ワールド位置（=弾の原点）。視差補正の基準。
            Vector3 camWorld = (Cfg.AimFromCameraOrigin && self.playerCamera != null)
                ? self.playerCamera.transform.position + Origin.position
                : self.getHeadPosition();

            // ★ (1) 射線が対象に届く狙点を探す
            Vector3 aimPoint;
            string  aimMode, bodyPart, reason;
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

            if (!shootable) // ★ 撃たない：遮蔽/FF/空。理由をログしてホールド
            {
                ReleaseFireIfPressed(self);
                if (Time.time >= _nextHoldLogTime)
                {
                    _nextHoldLogTime = Time.time + Cfg.LogThrottleSec;
                    Log.Out(string.Format(
                        "[CompanionAI] hold: {0} id={1} d={2:0.0}m reason={3}",
                        threat.Kind, tgt.entityId, d, reason));
                }
                return;
            }

            // ★ (2) 発砲準備：ADS＋カメラを狙点へスナップ
            Vector3 shotDir = aimPoint - camWorld;
            SetAds(self, true);
            if (Cfg.SnapCameraOnFire && self.playerCamera != null && shotDir.sqrMagnitude > 1e-6f)
            {
                self.playerCamera.transform.rotation =
                    Quaternion.LookRotation(shotDir.normalized, Vector3.up);
            }

            // ★ (3) フルオート判定して駆動
            bool fullAuto = Cfg.FullAutoHold && IsFullAuto(self);

            if (fullAuto)
            {
                // トリガー保持：毎フレーム press（RPM はゲートの Delay が律速）。離しは disengage 時のみ。
                int before = GetHoldingMeta(self);
                self.Attack(false);
                _firePressed = true;
                FireLog(self, threat, d, aimMode, bodyPart, true, GetHoldingMeta(self), before);
            }
            else
            {
                // セミ/バースト：press(N)→release(N+1) を FireInterval ごと
                if (_firePressed) { self.Attack(true); _firePressed = false; return; }
                if (Time.time < _nextFireTime) return;
                int before = GetHoldingMeta(self);
                self.Attack(false);
                _firePressed = true;
                _nextFireTime = Time.time + Cfg.RangedFireIntervalSec;
                FireLog(self, threat, d, aimMode, bodyPart, false, GetHoldingMeta(self), before);
            }
        }

        // 発砲/リロードのログ。Meta 差で実発砲を検出し、命中エンティティ(MinEventContext.Other)を突合。
        private static void FireLog(EntityPlayerLocal self, in ThreatInfo threat, float d,
                                    string aimMode, string bodyPart, bool fullAuto, int after, int before)
        {
            if (after < 0 || before < 0) return;
            if (after < before) // 実発砲
            {
                Entity hitE = self.MinEventContext != null ? self.MinEventContext.Other : null;
                string hitDesc = (hitE == null) ? "none"
                    : (hitE.entityId == threat.Target.entityId ? "TARGET" : "OTHER id=" + hitE.entityId);
                Log.Out(string.Format(
                    "[CompanionAI] fire: {0} id={1} d={2:0.0}m mag={3} aim={4}({5}) auto={6} ads={7} -> hit={8}",
                    threat.Kind, threat.Target.entityId, d, after, aimMode, bodyPart,
                    fullAuto ? "on" : "off", (self.AimingGun ? "on" : "off"), hitDesc));
            }
            else if (after == 0) { if (_lastMeta != 0) Log.Out("[CompanionAI] fire: empty — waiting for auto-reload."); }
            else if (_lastMeta >= 0 && after > _lastMeta) Log.Out(string.Format("[CompanionAI] reload: done, mag={0}", after));
            _lastMeta = after;
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

        // フルオート判定: GetBurstCount==0（BurstRoundCount 既定1=セミ, 0=フル, N=バースト）。
        private static bool IsFullAuto(EntityPlayerLocal self)
        {
            var inv = self.inventory;
            var hi  = inv != null ? inv.holdingItem : null;
            var hid = inv != null ? inv.holdingItemData : null;
            if (hi == null || hi.Actions == null || hi.Actions.Length == 0) return false;
            var ra = hi.Actions[0] as ItemActionRanged;
            if (ra == null || hid == null || hid.actionData == null || hid.actionData.Count == 0) return false;
            return ra.GetBurstCount(hid.actionData[0]) == 0;
        }

        // 保持中アイテムの装填残弾(Meta)。取得不可は -1。 (A4: holdingItemItemValue.Meta)
        private static int GetHoldingMeta(EntityPlayerLocal self)
        {
            var inv = self.inventory;
            var iv  = inv != null ? inv.holdingItemItemValue : null;
            return iv != null ? iv.Meta : -1;
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
                ModCfgFile.Reload();   // 編集した companion_config.txt を即反映
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
