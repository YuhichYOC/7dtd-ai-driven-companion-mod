/*
 *
 * CombatDriver.cs
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

using System.Collections.Generic;
using CompanionAIVerify.Combat.Action;
using CompanionAIVerify.Combat.Scene;
using CompanionAIVerify.Perception;
using CompanionAIVerify.Stance;
using UniLinq;

namespace CompanionAIVerify.Combat;

// --- Combat (engage slice) ----------------------------------------------
internal static class CombatDriver
{
    internal static ActionResolver ActionResolver;

    private static readonly List<ICombatAction> Actions =
    [
        new DrawPattern01(),
        new LauncherPattern01(),
        new MeleePattern01(),
        new TriggerPattern01()
    ];

    private static InfoHolder InfoHolder { get; } = new();

    // 現時点 ( ver 0.8.1 ) では、まだ外からこのメソッドを呼ぶ
    // 最終的には消したい
    internal static void ReleaseFireIfPressed(EntityPlayerLocal self)
    {
        InfoHolder.Self = self;
        InfoHolder.ReleaseOperator.Run();
    }

    // 交戦オーバーレイ。posture 決定の後に最後に呼ぶ（in-range 時の 3D エイムが
    // 平面 facing を同フレーム内で上書きするため）。
    internal static void OnCombatStep(EntityPlayerLocal self, in ThreatInfo threat)
    {
        InfoHolder.Self = self;
        InfoHolder.Target = threat;
        ResolveAction(ActionResolver.Action)?.Run(InfoHolder);
    }

    private static ICombatAction ResolveAction(ActionResolver.Actions action)
    {
        return action switch
        {
            ActionResolver.Actions.Draw01 => Actions.First(a => a.Name == "DrawPattern01"),
            ActionResolver.Actions.Launcher01 => Actions.First(a => a.Name == "LauncherPattern01"),
            ActionResolver.Actions.Melee01 => Actions.First(a => a.Name == "MeleePattern01"),
            ActionResolver.Actions.Trigger01 => Actions.First(a => a.Name == "TriggerPattern01"),
            _ => null
        };
    }
}