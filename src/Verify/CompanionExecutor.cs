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

extern alias LogLib;
extern alias UnityInputLegacy;
using CompanionAIVerify.Combat;
using CompanionAIVerify.Config;
using CompanionAIVerify.Perception;
using CompanionAIVerify.Positioning;
using CompanionAIVerify.Stance;
using CompanionAIVerify.ToolSelection;
using CompanionAIVerify.Utility;
using UnityEngine;

namespace CompanionAIVerify;

// --- Executor ------------------------------------------------------------
internal static class CompanionExecutor
{
    private static int _lastLoggedThreatId = int.MinValue;
    private static float _nextLogTime;
    private static float _nextJumpLogTime; // ★ [jump] 段差ジャンプ発火ログの throttle
    private static readonly ActionResolver ActionResolver = new();
    private static readonly PositionResolver PositionResolver = new();

    internal static void OnMovePrefix(EntityPlayerLocal self)
    {
        if (UnityInputLegacy::UnityEngine.Input.GetKeyDown(Cfg.ToggleKey))
        {
            ModCfgFile.Reload(); // 編集した companion_config.txt を即反映
            Cfg.Enabled = !Cfg.Enabled;
            LogLib::Log.Out("[CompanionAI] drive = " + Cfg.Enabled);
            if (!Cfg.Enabled)
            {
                CombatDriver.ReleaseFireIfPressed(self);
                Stop(self);
            }
        }

        if (!Cfg.Enabled) return;

        var world = GameManager.Instance != null ? GameManager.Instance.World : null;
        if (world == null || self != world.GetPrimaryPlayer()) return;

        var leader = FindNearestLeader(world, self);
        if (leader == null)
        {
            CombatDriver.ReleaseFireIfPressed(self);
            Stop(self);
            return;
        }

        if (Cfg.Enabled)
        {
            WeaponSelector.RefreshLoadout(self, true);
            ItemStower.MaybeRun(self, true);
        }

        ItemStower.MaybeRun(self, false);
        LeaderItemPickup.MaybeRun(self, leader);

        // --- 脅威検知（Section B） ---
        var threat = ThreatScanner.ScanNearestActiveThreat(world, self);
        LogThreat(threat);

        // v0.8.1 ロジック動的切り替え
        ActionResolver.Run(self, threat);
        PositionResolver.Run(self, threat);
        CombatDriver.ActionResolver = ActionResolver;

        // --- v0.8(B): 格闘オートアプローチ（follow より優先） ---
        //   格闘武器 かつ 交戦中脅威が「リーチ外 かつ approachMax 内」のとき、
        //   移動目標をリーダーから脅威へ差し替えて前進する（既存の Stop@standoff / Steer→leader を上書き）。
        //   接近steer の後に交戦オーバーレイを回して、リーチに入った瞬間から歩きながら振れるようにする。
        if (TryMeleeApproach(self, in threat))
        {
            CombatDriver.OnCombatStep(self, threat);
            return;
        }

        // --- posture: follow ---
        var flat = leader.position - self.position;
        flat.y = 0f;
        var dist = flat.magnitude;

        var lookDir = flat;
        if (Cfg.CombatMode && threat.Valid)
        {
            var toThreat = threat.Target.position - self.position;
            toThreat.y = 0f;
            if (toThreat.sqrMagnitude > 0.001f) lookDir = toThreat;
        }

        if (dist <= Cfg.StandoffMeters)
        {
            if (Cfg.CombatMode && threat.Valid) FaceOnly(self, lookDir);
            Stop(self);
        }
        else
        {
            // 既定は直線でリーダーへ。経路が届いていれば中間ウェイポイントへ向かう（navigation スライス3）。
            var moveTarget = leader.position;
            var pathActive = false;
            if (Cfg.PathFollow &&
                PathFollowState.TryGetMoveTarget(self.position, Cfg.WaypointArriveM, Cfg.WaypointHeightTolM,
                    Cfg.PathStaleSec, out var wpTarget, out var _pstatus))
            {
                moveTarget = wpTarget;
                pathActive = true;
            }

            // 戦闘中は脅威を向く(既存優先)。非戦闘の経路追従中のみ進行方向を向く。
            if (!(Cfg.CombatMode && threat.Valid) && pathActive)
            {
                var tdir = moveTarget - self.position;
                tdir.y = 0f;
                if (tdir.sqrMagnitude > 0.001f) lookDir = tdir;
            }

            Steer(self, moveTarget, lookDir, dist > Cfg.RunMeters);
        }

        // --- 交戦オーバーレイ（Section E）: 最後に実行 ---
        //   in-range の近接は 3D エイムで上の平面 facing を上書きしつつ press 駆動。
        CombatDriver.OnCombatStep(self, threat);
    }

    // v0.8(B): 格闘オートアプローチの判定＋実行。
    //   条件: MeleeAutoApproach ON / 交戦中脅威あり / 格闘武器保持 / reach < d <= approachMax。
    //   距離 d は CombatDriver の swing ゲートと同じ threat.DistSq(feet-to-feet)基準に揃える。
    //   reach は EngageRange の実効リーチ（Dynamic melee も正しく解決）。
    //   停止は d<=reach。swing は CombatDriver 側で reach+ReachBuffer から開くので、
    //   接近の最終区間は「歩きながら振る」→ reach で停止して振り続ける、と滑らかに繋がる。
    private static bool TryMeleeApproach(EntityPlayerLocal self, in ThreatInfo threat)
    {
        if (!Cfg.MeleeAutoApproach || !Cfg.CombatMode || !threat.Valid) return false;

        var er = EngageRange.Read(self);
        if (!er.valid || er.isRanged) return false; // 遠隔/無効は対象外（遠隔は RangedStep が担当）

        var reach = er.range > 0.01f ? er.range : 2.0f;
        var d = Mathf.Sqrt(threat.DistSq);

        // 停止距離はリーチより StepIn ぶん内側に置く。リーチ端(d≈reach)に張り付くと d_eyeChest>Range になりがちで
        // 空振りが混ざり、ターゲットの揺れで「振れるが届かない帯(reach〜reach+ReachBuffer)」に戻ってしまう。
        // 少し踏み込ませて d_eyeChest<Range の内側で安定させ、inRange を True に保つ（スイングの間欠発火＝スローペースも解消）。
        // 下限クランプ: StepIn を過大設定してもゾンビへ突っ込まないよう最低 0.8m は空ける。
        var stopDist = Mathf.Max(0.8f, reach - Cfg.MeleeApproachStepIn);

        // 停止距離内なら接近不要（その場で振る）。approachMax 外なら追わない（リーダーから離れ過ぎ防止）。
        if (d <= stopDist || d > Cfg.MeleeApproachMaxDistance) return false;

        var lookDir = threat.Target.position - self.position;
        lookDir.y = 0f;
        Steer(self, threat.Target.position, lookDir, false); // 数m の詰めは歩き（照準安定）
        return true;
    }

    private static void Steer(EntityPlayerLocal self, Vector3 moveTarget, Vector3 lookDir, bool running)
    {
        var toTarget = moveTarget - self.position;
        toTarget.y = 0f;
        if (toTarget.sqrMagnitude < 0.01f)
        {
            Stop(self);
            return;
        }

        var moveWorld = toTarget.normalized;

        var ld = lookDir;
        ld.y = 0f;
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

        var lookRight = Vector3.Cross(Vector3.up, lookFwd);
        self.movementInput.moveForward = Mathf.Clamp(Vector3.Dot(moveWorld, lookFwd), -1f, 1f);
        self.movementInput.moveStrafe = Mathf.Clamp(Vector3.Dot(moveWorld, lookRight), -1f, 1f);
        self.movementInput.running = running;

        // ★ [jump] 進行方向に「乗り越え可能な1ブロック段差」があればジャンプで越える。
        //   jump は EPL:3526 で inputWasJump とのエッジ比較＋onGround ゲート(EPL:3530)。詰まっている間 true を
        //   返し続けても、初回発火→空中(onGround=false で false)→着地で再評価、と自然に1回ずつジャンプする。
        self.movementInput.jump = ShouldJumpObstacle(self, moveWorld);
        self.movementInput.down = false;
    }

    // ★ [jump] 前方に「1ブロック段差（乗り越え可能）」があるかを判定し、先手でジャンプする。
    //   接地ゲートのみ: onGround は EPL:2589 で m_vp_FPController.Grounded から更新されるので EPL でも有効。
    //   ★ isCollidedHorizontally は使わない: それを更新する Entity:1834 は直後 1837 の m_characterController.IsGrounded() と
    //     同じ CharacterController 経路にあり、vp_FPController で動く EntityPlayerLocal では通らず常に false のため
    //     （実ログで確認: 段差手前でも not collidedHorizontally が連続）。よって「詰まってから」ではなく
    //     前方ボクセル探査で「詰まる前に」段差を検出して跳ぶ。
    //   乗り越え可否: 進行方向の前方セルで「脛の高さ(+0.5m)にブロック」かつ「頭の高さ(+1.5m)は空」＝段差1ブロック。
    //     2ブロック以上の壁は頭の高さが塞がるので false（無駄ジャンプを抑止）。高さオフセットは position.y の
    //     微小揺れ（地面上面の丸め）に強い +0.5/+1.5 を採用。階段状(1→2→3)は各段が1ブロック差なので順に登れる。
    //   ※ 検証フェーズ: 座標変換(Origin要否/高さ/プローブ)が正しいか実機で追えるよう毎回ログする。安定後に削る。
    private static bool ShouldJumpObstacle(EntityPlayerLocal self, Vector3 moveDir)
    {
        if (!Cfg.JumpObstacles) return false;
        if (!self.onGround) return false;

        var world = self.world;
        if (world == null) return false;

        var flat = moveDir;
        flat.y = 0f;
        if (flat.sqrMagnitude < 1e-4f) return false; // 前進意図なし
        flat.Normalize();

        // Entity.position はワールド座標（World 内の worldToBlockPos(_position) 呼び出し群と同じ扱い）。
        //   ※ CombatDriver で Origin を足したのは playerCamera.transform.position が Unity レンダ座標だったため。
        //     Entity.position には Origin 補正は不要。Origin.position はログにだけ残し、非ゼロ環境で気付けるようにする。
        var wp = self.position;
        var ahead = wp + flat * Cfg.JumpProbeAhead;

        var legCell = World.worldToBlockPos(new Vector3(ahead.x, wp.y + 0.5f, ahead.z)); // 脛の高さ
        var headCell = World.worldToBlockPos(new Vector3(ahead.x, wp.y + 1.5f, ahead.z)); // 頭の高さ

        var legBlocked = IsBlocking(world, legCell.x, legCell.y, legCell.z);
        var headClear = !IsBlocking(world, headCell.x, headCell.y, headCell.z);
        var jump = legBlocked && headClear;

        if (Time.time >= _nextJumpLogTime)
        {
            _nextJumpLogTime = Time.time + Cfg.LogThrottleSec;
            LogLib::Log.Out(
                $"[CompanionAI] pre-jump: pos=({wp.x:0.00},{wp.y:0.00},{wp.z:0.00}) originY={Origin.position.y:0.00} " +
                $"fwd=({flat.x:0.0},{flat.z:0.0}) probe={Cfg.JumpProbeAhead:0.0} " +
                $"leg=({legCell.x},{legCell.y},{legCell.z})blk={legBlocked} " +
                $"head=({headCell.x},{headCell.y},{headCell.z})clr={headClear} -> jump={jump}");
        }

        return jump;
    }

    // セルが移動を阻害するか。air は通行可、IsCollideMovement=true の実体ブロックのみ阻害。
    //   BlockValue.isair / Block.IsCollideMovement は vanilla の衝突判定と同じ経路（World:2072）。
    private static bool IsBlocking(World world, int x, int y, int z)
    {
        var bv = world.GetBlock(x, y, z);
        if (bv.isair) return false;
        var b = bv.Block;
        return b != null && b.IsCollideMovement;
    }

    private static void FaceOnly(EntityPlayerLocal self, Vector3 lookDir)
    {
        var ld = lookDir;
        ld.y = 0f;
        if (ld.sqrMagnitude > 0.001f) FaceWorldDir(self, ld.normalized);
    }

    private static void FaceWorldDir(EntityPlayerLocal self, Vector3 worldDir)
    {
        if (worldDir.sqrMagnitude < 1e-6f) return;
        var euler = Quaternion.LookRotation(worldDir.normalized, Vector3.up).eulerAngles;
        euler.x *= -1f;
        self.SetRotation(euler);
    }

    private static void Stop(EntityPlayerLocal self)
    {
        self.movementInput.moveForward = 0f;
        self.movementInput.moveStrafe = 0f;
        self.movementInput.running = false;
        self.movementInput.jump = false;
        self.movementInput.down = false;
    }

    private static EntityPlayer FindNearestLeader(World world, EntityPlayerLocal self)
    {
        var players = world.GetPlayers();
        if (players == null) return null;
        EntityPlayer best = null;
        var bestSq = float.MaxValue;
        for (var i = 0; i < players.Count; i++)
        {
            var p = players[i];
            if (p == null || p == self || p.IsDead()) continue;
            var sq = (p.position - self.position).sqrMagnitude;
            if (sq < bestSq)
            {
                bestSq = sq;
                best = p;
            }
        }

        return best;
    }

    private static void LogThreat(ThreatInfo t)
    {
        var id = t.Valid ? t.Target.entityId : int.MinValue;
        var changed = id != _lastLoggedThreatId;
        if (!changed && Time.time < _nextLogTime) return;

        _lastLoggedThreatId = id;
        _nextLogTime = Time.time + Cfg.LogThrottleSec;

        if (t.Valid)
        {
            var d = Mathf.Sqrt(t.DistSq);
            LogLib::Log.Out(
                $"[CompanionAI] threat: {t.Kind} {t.State} d={d:0.0}m (hostiles={ThreatScanner.LastHostileCount}, sleeping={ThreatScanner.LastSleepingCount})");
        }
        else
        {
            LogLib::Log.Out(
                $"[CompanionAI] threat: none (hostiles={ThreatScanner.LastHostileCount}, sleeping={ThreatScanner.LastSleepingCount})");
        }
    }
}