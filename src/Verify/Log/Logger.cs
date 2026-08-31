/*
 *
 * Logger.cs
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

extern alias LogLib;
using System.Linq;
using CompanionAIVerify.AstarPath;
using CompanionAIVerify.Combat.Scene;
using CompanionAIVerify.Config;
using CompanionAIVerify.Perception;
using CompanionAIVerify.Positioning;
using UnityEngine;

namespace CompanionAIVerify.Log;

internal static class Logger
{
    internal static void LogModLoaded()
    {
        PrintLog(
            $"[CompanionAI] verify harness v{Cfg.ModVersion} loaded " +
            $"(engage[melee+ranged/parallax/ADS/shootable-gate/full-auto] + file-config). " +
            $"F8 to toggle drive / reload config."
        );
    }

    internal static void LogModEnabled()
    {
        PrintLog($"[CompanionAI] drive = {Cfg.Enabled}");
    }

    internal static void LogLeaderFound(int id)
    {
        PrintLog($"[CompanionAI] leader found {id}");
    }

    internal static void LogHostPathProveToggle()
    {
        PrintLog($"[CompanionAI][host] path-probe = {HostProbeCfg.Enabled}");
    }

    internal static void LogHostPathProveRemoteNotFound()
    {
        PrintLog("[CompanionAI][host] no companion (remote player) found");
    }

    internal static void LogHostPathProveRemoteIsTooFar(float distance)
    {
        PrintLog(
            $"[CompanionAI][host] companion too far ({distance:0.0}m > {HostProbeCfg.MaxCompanionDist:0}m); skip request"
        );
    }

    internal static void LogHostPathProveNavigatorCreated(string name, int id)
    {
        PrintLog($"[CompanionAI][host] assigned navigator to companion '{name}' ({id})");
    }

    internal static void LogHostPathProveWaypointsSend(string status, string name, int id, int n, float straight,
        float poly, float endGap, float bestEndGap, float sinceProgress, Vector3 start, Vector3 end)
    {
        PrintLog(
            $"[CompanionAI][host] path {status} comp='{name}'({id}) " +
            $"pts={n} straight={straight:0.0}m poly={poly:0.0}m endGap={endGap:0.0}m " +
            $"bestGap={bestEndGap}m sinceProg={sinceProgress:0.0}s " +
            $"start=({start.x:0.0},{start.y:0.0},{start.z:0.0}) end=({end.x:0.0},{end.y:0.0},{end.z:0.0})"
        );
    }

    internal static void LogHostPathProveNoPath(int id, float dist, string reason)
    {
        PrintLog(
            $"[CompanionAI][host] path FAIL comp={id} d={dist:0.0}m : {reason} " +
            $"(グリッド76m外 / 未接続 / 生成失敗のいずれか)"
        );
    }

    internal static void LogHostPathProveHeartbeat(int selfId, bool worldIsNotNull, bool isRemote, bool hasPrimary,
        int players, bool pathFinderThreadIsNotNull)
    {
        PrintLog(
            $"[CompanionAI][host] hook alive: self={selfId} " +
            $"world={worldIsNotNull} isRemote={isRemote} primary={hasPrimary} players={players} " +
            $"astar={AstarManager.Instance != null} pft={pathFinderThreadIsNotNull} " +
            $"enabled={HostProbeCfg.Enabled} focus={Application.isFocused}"
        );
    }

    internal static void LogHostPathProveEdge()
    {
        PrintLog("[CompanionAI][host] Input.anyKeyDown edge seen here");
    }

    internal static void LogPathWireReception(int id, string status, int pts, int chunks, Vector3 start, Vector3 end)
    {
        PrintLog(
            $"[CompanionAI][host] sent path msgId={id} status={status} pts={pts} " +
            $"chunks={chunks} start=({start.x:0.0},{start.y:0.0},{start.z:0.0}) end=({end.x:0.0},{end.y:0.0},{end.z:0.0})"
        );
    }

    internal static void LogPathWireBrokenPoint(string p)
    {
        PrintWarn($"[CompanionAI][client] malformed point '{p}'");
    }

    internal static void LogPathWireReception(int id, string status, int pts, int exp, string verdict, int chunks,
        Vector3 start, Vector3 end)
    {
        PrintLog(
            $"[CompanionAI][client] recv path msgId={id} status={status} " +
            $"pts={pts}(exp {exp}) {verdict} chunks={chunks} " +
            $"start=({start.x:0.0},{start.y:0.0},{start.z:0.0}) end=({end.x:0.0},{end.y:0.0},{end.z:0.0})"
        );
    }

    internal static void LogPathWirePruneStale(int id)
    {
        PrintWarn($"[CompanionAI][client] dropped incomplete msgId={id} (got<total, stale)");
    }

    internal static void LogPathWireClientReceivedQuorumLack()
    {
        PrintWarn("[CompanionAI][client] malformed chunk (fields < 8)");
    }

    internal static void LogPathWireClientReceivedBrokenHeader()
    {
        PrintWarn("[CompanionAI][client] malformed chunk (header parse)");
    }

    internal static void LogPathWireClientReceivedBrokenWaypoint()
    {
        PrintWarn("[CompanionAI][client] malformed chunk (anchor)");
    }

    internal static void LogModNotFound()
    {
        PrintWarn("[CompanionAI] mod path unknown; config disabled, using defaults.");
    }

    internal static void LogConfigTemplateWritten(string path)
    {
        PrintLog($"[CompanionAI] wrote default config: {path}");
    }

    internal static void LogConfigTemplateCantWrite(string message)
    {
        PrintWarn($"[CompanionAI] could not write default config: {message}");
    }

    internal static void LogConfigLoadError(string message)
    {
        PrintWarn($"[CompanionAI] config init failed: {message} (using defaults)");
    }

    internal static void LogConfigReloadError(string message)
    {
        PrintWarn($"[CompanionAI] config reload failed: {message}");
    }

    internal static void LogConfigFileNotFound()
    {
        PrintLog("[CompanionAI] config not found, using defaults.");
    }

    internal static void LogConfigUnknownEntry(string key, string value)
    {
        PrintWarn($"[CompanionAI] config: unknown/invalid '{key}' = '{value}'");
    }

    internal static void LogConfigLoad(int applied)
    {
        PrintLog(
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
            $"AimAssist={Cfg.MeleeAimAssist} PinLeader={Cfg.DebugPinTargetToLeader} Freeze={Cfg.DebugFreezeHostiles}"
        );
    }

    internal static void LogRange(bool bareHand, int id, string ranged, string actionType, float range,
        float blockRange, float sphereRadius, string eyeChestDistance, bool inRange)
    {
        if (bareHand)
            PrintLog(
                $"[CompanionAI][EngageRange] eid={id} weapon=INVALID (no attack action / bare hand)"
            );
        else
            PrintLog(
                $"[CompanionAI][EngageRange] eid={id} {ranged} action={actionType} range={range:F2} " +
                $"block={blockRange:F2} sphere={sphereRadius:F2} d_eyeChest={eyeChestDistance} inRange={inRange}"
            );
    }

    internal static void LogWeaponClassifyStrictNoIncludes()
    {
        if (!(Time.time >= LogInfoHolder.NextWeaponClassifyLogTime)) return;
        LogInfoHolder.NextWeaponClassifyLogTime = Time.time + Cfg.LogThrottleSec;
        PrintWarn("[CompanionAI] weapon-classify: strict モードだが include 指定が空 = 全アイテム非武器扱い。");
    }

    internal static void LogWeaponClassify(int meleeNames, int rangedNames, int excludeNames, int hasMeleeTags,
        int hasRangedTags, int hasExcludeTags)
    {
        if (!(Time.time >= LogInfoHolder.NextWeaponClassifyLogTime)) return;
        LogInfoHolder.NextWeaponClassifyLogTime = Time.time + Cfg.LogThrottleSec;
        PrintLog(
            $"[CompanionAI] weapon-classify: mode={Cfg.WeaponClassifyMode} meleeNames={meleeNames} rangedNames={rangedNames} " +
            $"excludeNames={excludeNames} tags(m/r/x)={hasMeleeTags}/{hasRangedTags}/{hasExcludeTags}"
        );
    }

    internal static void LogWeaponSwitch(string mode, int slot, float distance, int rangedSlot, int meleeSlot)
    {
        PrintLog(
            $"[CompanionAI] weapon-switch: -> {mode} slot={slot} d={distance:0.0}m (R={rangedSlot} M={meleeSlot})"
        );
    }

    internal static void LogStowBag(int i, int slot, string stack)
    {
        PrintLog($"[CompanionAI] stow: bag[{i}] -> toolbelt[{slot}] {stack}");
    }

    internal static void LogStowToolbelt(int moved)
    {
        PrintLog($"[CompanionAI] stow: moved {moved} weapon stack(s) to toolbelt.");
    }

    internal static void LogPickup(int entityId, int playerId, float distance, string leaderOwned)
    {
        PrintLog(
            $"[CompanionAI] pickup: collect id={entityId} owner={playerId} d={distance:0.0}m ({leaderOwned})"
        );
    }

    internal static void LogPickupDiagnostics(int seen, int firstOwner, int leaderId, int selfId)
    {
        PrintLog(
            $"[CompanionAI] pickup: {seen} item(s) in range, none matched (firstOwner={firstOwner}, leaderId={leaderId}, selfId={selfId})."
        );
    }

    internal static void LogJump(Vector3 wp, Vector3 flat, Vector3 legCell, bool legBlocked, Vector3 headCell,
        bool headClear, bool jump)
    {
        if (!(Time.time >= LogInfoHolder.NextJumpLogTime)) return;
        LogInfoHolder.NextJumpLogTime = Time.time + Cfg.LogThrottleSec;
        PrintLog(
            $"[CompanionAI] pre-jump: pos=({wp.x:0.00},{wp.y:0.00},{wp.z:0.00}) originY={Origin.position.y:0.00} " +
            $"fwd=({flat.x:0.0},{flat.z:0.0}) probe={Cfg.JumpProbeAhead:0.0} " +
            $"leg=({legCell.x},{legCell.y},{legCell.z})blk={legBlocked} " +
            $"head=({headCell.x},{headCell.y},{headCell.z})clr={headClear} -> jump={jump}"
        );
    }

    internal static void LogThreat(ThreatInfo t)
    {
        var id = t.Valid ? t.Target.entityId : int.MinValue;
        var changed = id != LogInfoHolder.LastLoggedThreatId;
        if (!changed && !(Time.time >= LogInfoHolder.NextThreatLogTime)) return;
        LogInfoHolder.LastLoggedThreatId = id;
        LogInfoHolder.NextThreatLogTime = Time.time + Cfg.LogThrottleSec;
        PrintLog(
            t.Valid
                ? $"[CompanionAI] threat: {t.Kind} {t.State} d={Mathf.Sqrt(t.DistSq):0.0}m (hostiles={ThreatScanner.LastHostileCount}, sleeping={ThreatScanner.LastSleepingCount})"
                : $"[CompanionAI] threat: none (hostiles={ThreatScanner.LastHostileCount}, sleeping={ThreatScanner.LastSleepingCount})"
        );
    }

    internal static void LogMeleeSwing(InfoHolder i)
    {
        if (!(Time.time >= LogInfoHolder.NextEngageLogTime)) return;
        LogInfoHolder.NextEngageLogTime = Time.time + Cfg.LogThrottleSec;
        PrintLog(
            $"[CompanionAI] engage: swing {i.Target.Kind} {i.Target.State} d={i.Distance:0.0}m reach={i.Reach:0.0}m"
        );
    }

    internal static void LogRangedProhibited(InfoHolder i)
    {
        if (!(Time.time >= LogInfoHolder.NextEngageLogTime)) return;
        LogInfoHolder.NextEngageLogTime = Time.time + 1.0f;
        PrintLog($"[CompanionAI] engage: ranged holding within reach (d={i.Distance:0.0}m) — fire disabled.");
    }

    internal static void LogTargetOutOfRange(InfoHolder i)
    {
        if (!(Time.time >= LogInfoHolder.NextHoldLogTime)) return;
        LogInfoHolder.NextHoldLogTime = Time.time + Cfg.LogThrottleSec;
        var erC = EngageRange.Read(i.Self);
        PrintLog(
            $"[CompanionAI] hold: {i.Target.Kind} id={i.Target.Target.entityId} d={i.Distance:0.0}m > fireMax={i.FireMax:0.0}m " +
            $"(range={erC.range:0.0} x{Cfg.RangedRangeSafety:0.00}, cap={Cfg.RangedMaxEngageMeters:0.0})"
        );
    }

    internal static void LogShootableNotFound(InfoHolder i)
    {
        if (!(Time.time >= LogInfoHolder.NextHoldLogTime)) return;
        LogInfoHolder.NextHoldLogTime = Time.time + Cfg.LogThrottleSec;
        PrintLog(
            $"[CompanionAI] hold: {i.Target.Kind} id={i.Target.Target.entityId} d={i.Distance:0.0}m reason={i.AimOperator.Reason}"
        );
    }

    internal static void LogFriendlyInLineOfFire(InfoHolder i)
    {
        if (!(Time.time >= LogInfoHolder.NextHoldLogTime)) return;
        LogInfoHolder.NextHoldLogTime = Time.time + Cfg.LogThrottleSec;
        PrintLog(
            $"[CompanionAI] hold: {i.Target.Kind} id={i.Target.Target.entityId} d={i.Distance:0.0}m reason=FF id={i.ShootableValidator.FfFriendlies.First().entityId}"
        );
    }

    internal static void LogFire(InfoHolder i, bool fullAuto, int after, int before)
    {
        if (after < 0 || before < 0) return;
        if (after < before) // 実発砲
        {
            Entity hitE = i.Self.MinEventContext?.Other;
            var hitDesc = hitE == null ? "none"
                : hitE.entityId == i.Target.Target.entityId ? "TARGET" : "OTHER id=" + hitE.entityId;
            PrintLog(
                $"[CompanionAI] fire: {i.Target.Kind} id={i.Target.Target.entityId} d={i.Distance:0.0}m " +
                $"mag={after} aim={i.AimOperator.Mode}({i.AimOperator.Part}) auto={(fullAuto ? "on" : "off")} " +
                $"ads={(i.Self.AimingGun ? "on" : "off")} -> hit={hitDesc}"
            );
        }
        else if (after == 0)
        {
            if (LogInfoHolder.LastMeta != 0)
                PrintLog("[CompanionAI] fire: empty — waiting for auto-reload.");
        }
        else if (LogInfoHolder.LastMeta >= 0 && after > LogInfoHolder.LastMeta)
        {
            PrintLog($"[CompanionAI] reload: done, mag={after}");
        }

        LogInfoHolder.LastMeta = after;
    }

    internal static void LogFpv(InfoHolder i, bool fpv)
    {
        PrintLog(
            $"[CompanionAI] engage-precheck: bFirstPersonView={fpv} TPCam={i.Self.TPCameraCheckResult} camPassed={i.Self.TPCameraCheckPassed}"
        );
    }

    internal static void LogFpv(InfoHolder i)
    {
        PrintLog("[CompanionAI] engage-precheck: forced bFirstPersonView=true (ForceFirstPerson).");
    }

    internal static void LogStartDraw(InfoHolder i, int before, float maxStrainTime)
    {
        if (!(Time.time >= LogInfoHolder.NextBowLogTime)) return;
        LogInfoHolder.NextBowLogTime = Time.time + Cfg.LogThrottleSec;
        PrintLog(
            $"[CompanionAI] bow: draw-start {i.Target.Kind} id={i.Target.Target.entityId} d={i.Distance:0.0}m " +
            $"mag={before} maxStrain={maxStrainTime:0.00}s frac={Cfg.BowDrawFraction:0.00}"
        );
    }

    internal static void LogBowReload(InfoHolder i)
    {
        if (!(Time.time >= LogInfoHolder.NextBowLogTime)) return;
        LogInfoHolder.NextBowLogTime = Time.time + Cfg.LogThrottleSec;
        PrintLog($"[CompanionAI] bow: hold (no draw) mag={i.GetHoldingMeta()} — reload/delay.");
    }

    internal static void LogBowLoose(InfoHolder i, float strain, int after, string hit)
    {
        PrintLog(
            $"[CompanionAI] bow: loose {i.Target.Kind} id={i.Target.Target.entityId} d={i.Distance:0.0}m " +
            $"strain={strain:0.00} mag={after} aim={i.AimOperator.Mode}({i.AimOperator.Part}) ads={(i.Self.AimingGun ? "on" : "off")} -> hit={hit}"
        );
    }

    // [診断用ログ出力] 発射直前の攻撃レイ原点 / 方向を実測
    //   originYtoFeet = 原点の足元からの高さ / behind = 照準水平方向に対し前(+)か背後(-)か
    //   判定 : fpv = false もしくは ( originYtoFeet が低い & behind < 0 ) なら
    //         非 FPV ( 後方上方カメラ ) 由来の origin ずれ = 今回の「足元やや背後」を実証
    internal static void LogRayProbe(InfoHolder i)
    {
        var r = i.Self.GetLookRay();
        var d = r.origin - i.Self.position; // Origin.position 非依存(両者 world 系)
        var f = i.Self.GetLookVector();
        var ff = new Vector3(f.x, 0f, f.z);
        if (ff.sqrMagnitude > 1e-6f) ff.Normalize();
        var behind = Vector3.Dot(new Vector3(d.x, 0f, d.z), ff);
        PrintLog(
            $"[CompanionAI] ray-probe: fpv={i.Self.bFirstPersonView} " +
            $"camDist={i.Self.vp_FPCamera.CurrentCameraDistance:0.00} " +
            $"originYtoFeet={d.y:0.00} behind={behind:0.00} " +
            $"dir=({r.direction.x:0.00},{r.direction.y:0.00},{r.direction.z:0.00})"
        );
    }

    internal static void LogDebug(string message)
    {
        LogLib::Log.Out(message);
    }

    private static void PrintLog(string message)
    {
        LogLib::Log.Out(message);
    }

    private static void PrintWarn(string message)
    {
        LogLib::Log.Warning(message);
    }
}