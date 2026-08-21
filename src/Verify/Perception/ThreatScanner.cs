/*
*
* ThreatScanner.cs
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
using UnityEngine;

using CompanionAIVerify.Config;

namespace CompanionAIVerify.Perception
{
    // --- Threat sensing ------------------------------------------------------
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
}
