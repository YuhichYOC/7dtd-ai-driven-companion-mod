/*
*
* CompanionExecutor.cs
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

using UnityEngine;

namespace CompanionAIVerify
{
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
                if (!Cfg.Enabled)
                {
                    CombatDriver.ReleaseIfPressed(self);
                    CombatDriver.ReleaseFireIfPressed(self);
                    Stop(self);
                }
            }
            if (!Cfg.Enabled) return;

            var world = GameManager.Instance != null ? GameManager.Instance.World : null;
            if (world == null || self != world.GetPrimaryPlayer()) return;

            EntityPlayer leader = FindNearestLeader(world, self);
            if (leader == null) { CombatDriver.ReleaseIfPressed(self); CombatDriver.ReleaseFireIfPressed(self); Stop(self); return; }

            if (Cfg.Enabled) { WeaponSelector.RefreshLoadout(self, force: true); ItemStower.MaybeRun(self, force: true); }

            ItemStower.MaybeRun(self, force: false);
            LeaderItemPickup.MaybeRun(self, leader);

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
                Log.Out($"[CompanionAI] threat: {t.Kind} {t.State} d={d:0.0}m (hostiles={ThreatScanner.LastHostileCount}, sleeping={ThreatScanner.LastSleepingCount})");
            }
            else
            {
                Log.Out($"[CompanionAI] threat: none (hostiles={ThreatScanner.LastHostileCount}, sleeping={ThreatScanner.LastSleepingCount})");
            }
        }
    }
}
