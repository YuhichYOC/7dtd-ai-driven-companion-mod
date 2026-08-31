/*
 *
 * PlayerScanner.cs
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

using System.Linq;

namespace CompanionAIVerify.Perception;

internal static class PlayerScanner
{
    internal static EntityPlayer FindNearestLeader(World world, EntityPlayerLocal self)
    {
        var players = world.GetPlayers();
        if (players == null || players.Count == 0) return null;
        return players
            .Where(p => p != null)
            .Where(p => p != self)
            .Where(p => !p.IsDead())
            .OrderBy(p => (p.position - self.position).sqrMagnitude)
            .FirstOrDefault();
    }
}