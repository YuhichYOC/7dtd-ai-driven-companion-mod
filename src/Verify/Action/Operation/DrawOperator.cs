/*
 *
 * DrawOperator.cs
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

using System;
using CompanionAIVerify.Action.Scene;
using CompanionAIVerify.Config;
using UnityEngine;
using Logger = CompanionAIVerify.Log.Logger;

namespace CompanionAIVerify.Action.Operation;

internal class DrawOperator
{
    private readonly InfoHolder _i;
    private Tuple<ItemActionCatapult, ItemActionCatapult.ItemActionDataCatapult> _bow;

    internal DrawOperator(InfoHolder i)
    {
        _i = i;
    }

    internal bool BowHolding => _bow.Item1 != null && _bow.Item2 != null;

    internal bool ActionActivated => _bow.Item2.m_bActivated;

    // ドロー中 ( ゲーム側 m_bActivated と同期 )
    internal bool Drawing { get; set; }

    // 前フレームで press した -> 今フレーム release
    internal bool Pressed { get; set; }

    // 発射直後の再ドロー抑制
    // 連射律速自体はゲーム Delay ... Catapult : 109 が担保
    internal float BowNextTry { get; set; }

    internal void Init()
    {
        _bow = _i.GetHeldCatapult();
    }

    internal void CancelDrawing(bool cancelAction)
    {
        if (cancelAction) _bow.Item1.CancelAction(_bow.Item2);
        Drawing = false;
    }

    internal void StartDraw()
    {
        // 発射直後の再ドロー抑制
        // 無駄 press とログの間引き。連射律速はゲーム Delay が担保
        if (Time.time < BowNextTry) return;

        var before = _i.GetHoldingMeta();
        _i.Self.Attack(false); // press -> ExecuteAction(false)

        if (_bow.Item2.m_bActivated)
        {
            Drawing = true; // ゲーム側が活性化 = ドロー開始成功
            Logger.LogStartDraw(_i, before, _bow.Item2.m_MaxStrainTime);
        }
        else
        {
            // 活性化せず = 矢切れでリロード要求 ( Catapult : 113-120 ) or Delay
            // 少し待って再試行
            BowNextTry = Time.time + Cfg.LogThrottleSec;
            Logger.LogBowReload(_i);
        }
    }

    internal void Draw()
    {
        // Drawing 中
        // ゲーム側 m_ActivateTime を基準に経過を測る ( Time.time 差でゲームと完全同期 )
        var maxStrain = _bow.Item2.m_MaxStrainTime > 0.01f ? _bow.Item2.m_MaxStrainTime : 2.0f;
        var need = maxStrain * Mathf.Clamp01(Cfg.BowDrawFraction);
        var elapsed = Time.time - _bow.Item2.m_ActivateTime;
        if (elapsed < need) return; // まだ引き絞り中 ( 狙点追従は前段で継続 )

        var before = _i.GetHoldingMeta();
        _i.Self.Attack(true); // release -> 発射
        Drawing = false;

        // 再ドロー抑制
        // Delay ( RPM由来, ItemActionDataRanged.Delay ) を尊重し、取れなければ FireInterval で代用
        var delay = _bow.Item2.Delay;
        BowNextTry = Time.time + (delay > 0.01f ? delay : Cfg.RangedFireIntervalSec);

        var after = _i.GetHoldingMeta();
        var strain = maxStrain > 0.01f ? Mathf.Clamp01(elapsed / maxStrain) : 1f;
        var hit = "none";
        if (after < before) // 実発砲 ( Meta 減 ) を検出して命中突合
        {
            Entity hitE = _i.Self.MinEventContext != null ? _i.Self.MinEventContext.Other : null;
            hit = hitE == null ? "none"
                : hitE.entityId == _i.Target.Target.entityId ? "TARGET" : "OTHER id=" + hitE.entityId;
        }

        Logger.LogBowLoose(_i, strain, after, hit);
    }
}