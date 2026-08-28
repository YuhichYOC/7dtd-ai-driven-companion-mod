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
using CompanionAIVerify.Combat.Scene;
using CompanionAIVerify.Config;
using CompanionAIVerify.Positioning;
using UnityEngine;

namespace CompanionAIVerify.Combat.Log;

internal class Logger
{
    private readonly LogInfoHolder _i;

    internal Logger(LogInfoHolder i)
    {
        _i = i;
    }

    internal void LogMeleeSwing(InfoHolder i)
    {
        if (Time.time >= _i.NextEngageLogTime)
        {
            _i.NextEngageLogTime = Time.time + Cfg.LogThrottleSec;
            PrintLog(
                $"[CompanionAI] engage: swing {i.Target.Kind} {i.Target.State} d={i.Distance:0.0}m reach={i.Reach:0.0}m"
            );
        }
    }

    internal void LogRangedProhibited(InfoHolder i)
    {
        if (Time.time >= _i.NextEngageLogTime)
        {
            _i.NextEngageLogTime = Time.time + 1.0f;
            PrintLog($"[CompanionAI] engage: ranged holding within reach (d={i.Distance:0.0}m) — fire disabled.");
        }
    }

    internal void LogTargetOutOfRange(InfoHolder i)
    {
        if (Time.time >= _i.NextHoldLogTime)
        {
            _i.NextHoldLogTime = Time.time + Cfg.LogThrottleSec;
            var erC = EngageRange.Read(i.Self);
            PrintLog(
                $"[CompanionAI] hold: {i.Target.Kind} id={i.Target.Target.entityId} d={i.Distance:0.0}m > fireMax={i.FireMax:0.0}m " +
                $"(range={erC.range:0.0} x{Cfg.RangedRangeSafety:0.00}, cap={Cfg.RangedMaxEngageMeters:0.0})"
            );
        }
    }

    internal void LogShootableNotFound(InfoHolder i)
    {
        if (Time.time >= _i.NextHoldLogTime)
        {
            _i.NextHoldLogTime = Time.time + Cfg.LogThrottleSec;
            PrintLog(
                $"[CompanionAI] hold: {i.Target.Kind} id={i.Target.Target.entityId} d={i.Distance:0.0}m reason={i.AimOperation.Reason}"
            );
        }
    }

    internal void LogFriendlyInLineOfFire(InfoHolder i)
    {
        if (Time.time >= _i.NextHoldLogTime)
        {
            _i.NextHoldLogTime = Time.time + Cfg.LogThrottleSec;
            PrintLog(
                $"[CompanionAI] hold: {i.Target.Kind} id={i.Target.Target.entityId} d={i.Distance:0.0}m reason=FF id={i.ShootableValidator.FfFriendlies.First().entityId}"
            );
        }
    }

    internal void LogFire(InfoHolder i, bool fullAuto, int after, int before)
    {
        if (after < 0 || before < 0) return;
        if (after < before) // 実発砲
        {
            Entity hitE = i.Self.MinEventContext != null ? i.Self.MinEventContext.Other : null;
            var hitDesc = hitE == null ? "none"
                : hitE.entityId == i.Target.Target.entityId ? "TARGET" : "OTHER id=" + hitE.entityId;
            PrintLog(
                $"[CompanionAI] fire: {i.Target.Kind} id={i.Target.Target.entityId} d={i.Distance:0.0}m " +
                $"mag={after} aim={i.AimOperation.Mode}({i.AimOperation.Part}) auto={(fullAuto ? "on" : "off")} " +
                $"ads={(i.Self.AimingGun ? "on" : "off")} -> hit={hitDesc}"
            );
        }
        else if (after == 0)
        {
            if (_i.LastMeta != 0)
                PrintLog("[CompanionAI] fire: empty — waiting for auto-reload.");
        }
        else if (_i.LastMeta >= 0 && after > _i.LastMeta)
        {
            PrintLog($"[CompanionAI] reload: done, mag={after}");
        }

        _i.LastMeta = after;
    }

    internal void LogFpv(InfoHolder i, bool fpv)
    {
        PrintLog(
            $"[CompanionAI] engage-precheck: bFirstPersonView={fpv} TPCam={i.Self.TPCameraCheckResult} camPassed={i.Self.TPCameraCheckPassed}"
        );
    }

    internal void LogFpv(InfoHolder i)
    {
        PrintLog("[CompanionAI] engage-precheck: forced bFirstPersonView=true (ForceFirstPerson).");
    }

    internal void LogStartDraw(InfoHolder i, int before, float maxStrainTime)
    {
        if (Time.time >= _i.NextBowLogTime)
        {
            _i.NextBowLogTime = Time.time + Cfg.LogThrottleSec;
            PrintLog(
                $"[CompanionAI] bow: draw-start {i.Target.Kind} id={i.Target.Target.entityId} d={i.Distance:0.0}m " +
                $"mag={before} maxStrain={maxStrainTime:0.00}s frac={Cfg.BowDrawFraction:0.00}"
            );
        }
    }

    internal void LogBowReload(InfoHolder i)
    {
        if (Time.time >= _i.NextBowLogTime)
        {
            _i.NextBowLogTime = Time.time + Cfg.LogThrottleSec;
            PrintLog($"[CompanionAI] bow: hold (no draw) mag={i.GetHoldingMeta()} — reload/delay.");
        }
    }

    internal void LogBowLoose(InfoHolder i, float strain, int after, string hit)
    {
        PrintLog(
            $"[CompanionAI] bow: loose {i.Target.Kind} id={i.Target.Target.entityId} d={i.Distance:0.0}m " +
            $"strain={strain:0.00} mag={after} aim={i.AimOperation.Mode}({i.AimOperation.Part}) ads={(i.Self.AimingGun ? "on" : "off")} -> hit={hit}"
        );
    }

    private void PrintLog(string message)
    {
        LogLib::Log.Out(message);
    }
}