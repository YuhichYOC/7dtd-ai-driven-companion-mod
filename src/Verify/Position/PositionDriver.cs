/*
 *
 * PositionDriver.cs
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
using System.Linq;
using CompanionAIVerify.Perception;
using CompanionAIVerify.Position.Pattern;
using CompanionAIVerify.Position.Scene;
using CompanionAIVerify.Stance;

namespace CompanionAIVerify.Position;

internal static class PositionDriver
{
    internal static PositionResolver PositionResolver;

    private static readonly List<IPositionPattern> Patterns =
    [
        new FollowPattern01(),
        new FollowPattern02(),
        new MeleePattern01()
    ];

    private static InfoHolder InfoHolder { get; } = new();

    internal static void OnTick(EntityPlayerLocal self, EntityPlayer leader, in ThreatInfo threat)
    {
        InfoHolder.Self = self;
        InfoHolder.Leader = leader;
        InfoHolder.Threat = threat;
        ResolvePosition(PositionResolver.Action)?.Run(InfoHolder);
    }

    private static IPositionPattern ResolvePosition(PositionResolver.Actions action)
    {
        return action switch
        {
            PositionResolver.Actions.Follow01 => Patterns.First(p => p.Name == "FollowPattern01"),
            PositionResolver.Actions.Follow02 => Patterns.First(p => p.Name == "FollowPattern02"),
            PositionResolver.Actions.Melee01 => Patterns.First(p => p.Name == "MeleePattern01"),
            _ => null
        };
    }
}