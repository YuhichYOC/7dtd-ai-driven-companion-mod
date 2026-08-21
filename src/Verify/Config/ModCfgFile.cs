/*
*
* ModCfgFile.cs
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

namespace CompanionAIVerify.Config
{
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
            Log.Out(
                $"[CompanionAI] config loaded ({applied} keys): " +
                $"Combat={Cfg.CombatMode} Ranged={Cfg.EnableRangedFire} Snap={Cfg.SnapCameraOnFire} " +
                $"AimFromCam={Cfg.AimFromCameraOrigin} ADS={Cfg.AimDownSightsOnEngage} ForceFPV={Cfg.ForceFirstPerson} | " +
                $"Standoff={Cfg.StandoffMeters} Run={Cfg.RunMeters} ScanR={Cfg.ThreatScanRadius} " +
                $"HeadLift={Cfg.HeadAimMinLift} MaxEngage={Cfg.RangedMaxEngageMeters} FireInt={Cfg.RangedFireIntervalSec} " +
                $"RangeSafety={Cfg.RangedRangeSafety} FFGate={Cfg.FriendlyFireGate} FFMargin={Cfg.FriendlyFireMargin} " +
                $"ReachBuf={Cfg.ReachBuffer} LogThr={Cfg.LogThrottleSec} " +
                $"Bow={Cfg.BowChargeEnabled} BowFrac={Cfg.BowDrawFraction} " +
                $"Jump={Cfg.JumpObstacles} JumpProbe={Cfg.JumpProbeAhead} " +
                $"| Approach={Cfg.MeleeAutoApproach} ApproachMax={Cfg.MeleeApproachMaxDistance} StepIn={Cfg.MeleeApproachStepIn} " +
                $"AimAssist={Cfg.MeleeAimAssist} PinLeader={Cfg.DebugPinTargetToLeader} Freeze={Cfg.DebugFreezeHostiles}");
        }

        private static bool Apply(string key, string val)
        {
            switch (key)
            {
                case "CombatMode":                 return TryBool(val, ref Cfg.CombatMode);
                case "EnableRangedFire":           return TryBool(val, ref Cfg.EnableRangedFire);
                case "ForceFirstPerson":           return TryBool(val, ref Cfg.ForceFirstPerson);
                case "SnapCameraOnFire":           return TryBool(val, ref Cfg.SnapCameraOnFire);
                case "AimFromCameraOrigin":        return TryBool(val, ref Cfg.AimFromCameraOrigin);
                case "AimDownSightsOnEngage":      return TryBool(val, ref Cfg.AimDownSightsOnEngage);
                case "RequireShootable":           return TryBool(val, ref Cfg.RequireShootable);
                case "FullAutoHold":               return TryBool(val, ref Cfg.FullAutoHold);
                case "StandoffMeters":             return TryF(val, ref Cfg.StandoffMeters);
                case "RunMeters":                  return TryF(val, ref Cfg.RunMeters);
                case "PathFollow":                 return TryBool(val, ref Cfg.PathFollow);
                case "WaypointArriveM":            return TryF(val, ref Cfg.WaypointArriveM);
                case "WaypointHeightTolM":         return TryF(val, ref Cfg.WaypointHeightTolM);
                case "PathStaleSec":               return TryF(val, ref Cfg.PathStaleSec);
                // ★ [jump] 障害物ジャンプ（v0.8.1）
                case "JumpObstacles":              return TryBool(val, ref Cfg.JumpObstacles);
                case "JumpProbeAhead":             return TryF(val, ref Cfg.JumpProbeAhead);
                case "ThreatScanRadius":           return TryF(val, ref Cfg.ThreatScanRadius);
                case "LogThrottleSec":             return TryF(val, ref Cfg.LogThrottleSec);
                case "ReachBuffer":                return TryF(val, ref Cfg.ReachBuffer);
                case "HeadAimMinLift":             return TryF(val, ref Cfg.HeadAimMinLift);
                case "RangedMaxEngageMeters":      return TryF(val, ref Cfg.RangedMaxEngageMeters);
                case "RangedRangeSafety":          return TryF(val, ref Cfg.RangedRangeSafety);
                case "FriendlyFireGate":           return TryBool(val, ref Cfg.FriendlyFireGate);
                case "FriendlyFireMargin":         return TryF(val, ref Cfg.FriendlyFireMargin);
                case "RangedFireIntervalSec":      return TryF(val, ref Cfg.RangedFireIntervalSec);
                // ★ [bow] 弓/クロスボウ引き絞り（v0.8.1）
                case "BowChargeEnabled":           return TryBool(val, ref Cfg.BowChargeEnabled);
                case "BowDrawFraction":            return TryF(val, ref Cfg.BowDrawFraction);
                case "AutoWeaponSwitch":           return TryBool(val, ref Cfg.AutoWeaponSwitch);
                case "AutoStowWeaponsToToolbelt":  return TryBool(val, ref Cfg.AutoStowWeaponsToToolbelt);
                case "StowDynamicMelee":           return TryBool(val, ref Cfg.StowDynamicMelee);
                case "WeaponClassifyMode":
                {
                    string m = val.Trim().ToLowerInvariant();
                    if (m == "auto" || m == "strict") { Cfg.WeaponClassifyMode = m; return true; }
                    return false;
                }
                case "MeleeWeaponNames":   Cfg.MeleeWeaponNames   = val; return true;
                case "RangedWeaponNames":  Cfg.RangedWeaponNames  = val; return true;
                case "WeaponExcludeNames": Cfg.WeaponExcludeNames = val; return true;
                case "MeleeWeaponTags":    Cfg.MeleeWeaponTags    = val; return true;
                case "RangedWeaponTags":   Cfg.RangedWeaponTags   = val; return true;
                case "WeaponExcludeTags":  Cfg.WeaponExcludeTags  = val; return true;
                case "AutoPickupLeaderDrops":      return TryBool(val, ref Cfg.AutoPickupLeaderDrops);
                case "PickupUnowned":              return TryBool(val, ref Cfg.PickupUnowned);
                case "SwitchToMeleeMeters":        return TryF(val, ref Cfg.SwitchToMeleeMeters);
                case "SwitchToRangedMeters":       return TryF(val, ref Cfg.SwitchToRangedMeters);
                case "WeaponSwitchMinIntervalSec": return TryF(val, ref Cfg.WeaponSwitchMinIntervalSec);
                case "LoadoutScanIntervalSec":     return TryF(val, ref Cfg.LoadoutScanIntervalSec);
                case "ToolbeltStowIntervalSec":    return TryF(val, ref Cfg.ToolbeltStowIntervalSec);
                case "PickupRadius":               return TryF(val, ref Cfg.PickupRadius);
                case "PickupScanIntervalSec":      return TryF(val, ref Cfg.PickupScanIntervalSec);
                case "LogEngageRange":             return TryBool(val, ref Cfg.LogEngageRange);
                case "EngageLogMinInterval":       return TryF(val, ref Cfg.EngageLogMinInterval);
                case "MeleeAutoApproach":          return TryBool(val, ref Cfg.MeleeAutoApproach);
                case "MeleeApproachMaxDistance":   return TryF(val, ref Cfg.MeleeApproachMaxDistance);
                case "MeleeApproachStepIn":        return TryF(val, ref Cfg.MeleeApproachStepIn);
                case "MeleeAimAssist":             return TryBool(val, ref Cfg.MeleeAimAssist);
                case "MeleeAimAssistHoldTicks":    return TryInt(val, ref Cfg.MeleeAimAssistHoldTicks);
                case "DebugPinTargetToLeader":     return TryBool(val, ref Cfg.DebugPinTargetToLeader);
                case "DebugFreezeHostiles":        return TryBool(val, ref Cfg.DebugFreezeHostiles);
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

        private static bool TryInt(string s, ref int dst)
        {
            if (int.TryParse(s, System.Globalization.NumberStyles.Integer,
                             System.Globalization.CultureInfo.InvariantCulture, out int v))
            { dst = v; return true; }
            return false;
        }

        private static string DefaultText()
        {
            return
                $"# CompanionAI verify harness config (v{Cfg.ModVersion})\n" +
                $"# 変更後、ゲーム内で F8（ドライブ切替）を押すと再読込されます。起動時にも読込。\n" +
                $"# bool = true/false (1/0/on/off も可) , float = 小数点は '.'（例 3.0）\n" +
                $"\n" +
                $"# --- 交戦の基本 ---\n" +
                $"CombatMode                 = true\n" +
                $"EnableRangedFire           = true\n" +
                $"\n" +
                $"# --- 追従 ---\n" +
                $"StandoffMeters             = 3.0\n" +
                $"RunMeters                  = 8.0\n" +
                $"\n" +
                $"# --- 障害物ジャンプ（v0.8.1）---\n" +
                $"# 前進中に前方が詰まり、前方セルが1ブロック段差（脛にブロック/頭は空）ならジャンプで乗り越える。\n" +
                $"# 2ブロック以上の壁はジャンプしない。ProbeAhead は段差セルを見に行く前方距離(m)。\n" +
                $"JumpObstacles              = true\n" +
                $"JumpProbeAhead             = 0.6\n" +
                $"\n" +
                $"# --- 脅威検知 ---\n" +
                $"ThreatScanRadius           = 20.0\n" +
                $"LogThrottleSec             = 0.5\n" +
                $"\n" +
                $"# --- 近接 ---\n" +
                $"ReachBuffer                = 0.5\n" +
                $"\n" +
                $"# --- 狙点（頭/胴の切替しきい値, m）---\n" +
                $"HeadAimMinLift             = 1.2\n" +
                $"\n" +
                $"# --- 発砲 ---\n" +
                $"RangedMaxEngageMeters      = 18.0\n" +
                $"# 武器の実効射程×この係数までしか撃たない（弾が届かない距離での空撃ち防止）\n" +
                $"RangedRangeSafety          = 0.85\n" +
                $"# 射線帯に友軍(他プレイヤー+allyドローン)が居れば発砲しない。Margin は友軍AABBの片側膨張(m)\n" +
                $"FriendlyFireGate           = true\n" +
                $"FriendlyFireMargin         = 0.4\n" +
                $"RangedFireIntervalSec      = 0.4\n" +
                $"\n" +
                $"# --- 弓/クロスボウ引き絞り（ItemActionCatapult, v0.8.1）---\n" +
                $"# press でチャージ開始→フルドロー(m_MaxStrainTime)×Fraction まで引く→release で発射。\n" +
                $"# false にすると弓を撃たない（ドローなしでは矢が飛ばないためホールド）。\n" +
                $"BowChargeEnabled           = true\n" +
                $"# フルドローの何割まで引くか。strain は Clamp01 されないため 1.0 未満でオーバーチャージ回避（0.90-0.98 推奨）\n" +
                $"BowDrawFraction            = 0.95\n" +
                $"\n" +
                $"# --- カメラ/視差/ADS（A/B対象）---\n" +
                $"ForceFirstPerson           = false\n" +
                $"SnapCameraOnFire           = true\n" +
                $"AimFromCameraOrigin        = true\n" +
                $"AimDownSightsOnEngage      = true\n" +
                $"\n" +
                $"# --- hit検証ゲート / フルオート（v0.6.0）---\n" +
                $"RequireShootable           = true\n" +
                $"FullAutoHold               = true\n" +
                $"\n" +
                $"# --- 武器自動切替（v0.7(A)）---\n" +
                $"AutoWeaponSwitch           = true\n" +
                $"SwitchToMeleeMeters        = 3.5\n" +
                $"SwitchToRangedMeters       = 5.5\n" +
                $"WeaponSwitchMinIntervalSec = 0.6\n" +
                $"LoadoutScanIntervalSec     = 1.0\n" +
                $"\n" +
                $"# --- ツールベルト優先配置（v0.7(B)）---\n" +
                $"AutoStowWeaponsToToolbelt  = true\n" +
                $"ToolbeltStowIntervalSec    = 5.0\n" +
                $"StowDynamicMelee           = true\n" +
                $"\n" +
                $"# --- リーダー落下物拾得（v0.7(C)）---\n" +
                $"AutoPickupLeaderDrops      = true\n" +
                $"PickupRadius               = 6.0\n" +
                $"PickupScanIntervalSec      = 0.5\n" +
                $"PickupUnowned              = false\n" +
                $"\n" +
                $"# --- 交戦マニューバ（v0.8）---\n" +
                $"LogEngageRange             = true\n" +
                $"EngageLogMinInterval       = 0.5\n" +
                $"# 格闘オートアプローチ: リーチ外の交戦中脅威が MaxDistance 内なら自動接近\n" +
                $"MeleeAutoApproach          = true\n" +
                $"MeleeApproachMaxDistance   = 6.0\n" +
                $"# 接近の停止距離をリーチより内側へ(reach-StepIn)。リーチ端張り付きの空振り対策。大きいほど踏み込む\n" +
                $"MeleeApproachStepIn        = 0.7\n" +
                $"# 照準補正(A): SetAttackTarget で近接レイをチェストへ自動補正\n" +
                $"MeleeAimAssist             = true\n" +
                $"MeleeAimAssistHoldTicks    = 30\n" +
                $"# テスト用: ゾンビの標的をリーダーに固定（単独交戦で間合いを観察する用・通常は false）\n" +
                $"DebugPinTargetToLeader     = false\n" +
                $"# テスト用: 敵対をその場に固定（approachMax 検証で静止した交戦中ゾンビを置く用・通常は false）\n" +
                $"DebugFreezeHostiles        = false\n";
        }
    }
}
