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

extern alias UnityInputLegacy;
using CompanionAIVerify.Combat;
using CompanionAIVerify.Config;
using CompanionAIVerify.Perception;
using CompanionAIVerify.Positioning;
using CompanionAIVerify.Stance;
using CompanionAIVerify.ToolSelection;
using CompanionAIVerify.Utility;
using CompanionAIVerify.Utility.Debugging;
using UnityEngine;
using Logger = CompanionAIVerify.Log.Logger;

namespace CompanionAIVerify;

// --- Executor ------------------------------------------------------------
internal static class CompanionExecutor
{
    private static readonly ActionResolver ActionResolver = new();
    private static readonly PositionResolver PositionResolver = new();
    private static EntityPlayer _leader;
    private static ThreatInfo _threat;
    private static bool _weaponSwitched;

    internal static void OnMovePrefix(EntityPlayerLocal self)
    {
        if (!ReadToggleKey(self)) return;

        var world = GameManager.Instance != null ? GameManager.Instance.World : null;
        if (world == null || self != world.GetPrimaryPlayer()) return;

        if (!FindNearestLeader(world, self)) return;

        RunUtilities(self);
        RunRepositoryUtilities(self);
        FindThreat(world, self);
        RunResolvers(self);

        if (RunMeleePositioning(self)) return;

        RunFollowPositioning(self);
        RunCombatDriver(self);
    }

    #region 有効・無効

    private static bool ReadToggleKey(EntityPlayerLocal self)
    {
        if (!UnityInputLegacy::UnityEngine.Input.GetKeyDown(Cfg.ToggleKey)) return Cfg.Enabled;
        ModCfgFile.Reload(); // 編集した companion_config.txt を即反映
        Cfg.Enabled = !Cfg.Enabled;
        Logger.LogModEnabled();
        if (!Cfg.Enabled) TurnOff(self);

        return Cfg.Enabled;
    }

    #endregion

    #region リーダーの検索

    private static bool FindNearestLeader(World world, EntityPlayerLocal self)
    {
        if (_leader != null) return true;
        _leader = PlayerScanner.FindNearestLeader(world, self);
        if (_leader != null)
        {
            Logger.LogLeaderFound(_leader.entityId);
            return true;
        }

        TurnOff(self);
        return false;
    }

    #endregion

    #region モジュールの停止

    private static void TurnOff(EntityPlayerLocal self)
    {
        _leader = null;
        CombatDriver.ReleaseFireIfPressed(self);
        Stop(self);
        DebugOverlay.Hide();
    }

    #endregion

    #region 脅威検知

    private static void FindThreat(World world, EntityPlayerLocal self)
    {
        // --- 脅威検知（Section B） ---
        _threat = ThreatScanner.ScanNearestActiveThreat(world, self);
        Logger.LogThreat(_threat);
    }

    #endregion

    #region 制御切り替え

    private static void RunResolvers(EntityPlayerLocal self)
    {
        // v0.8.1 ロジック動的切り替え
        // v0.8.3 依存反転
        //   [ データ ] RefreshLoadout ( 上で実行済 ) -> [ 判断 ] ActionResolver -> [ 実行 ] WeaponSelector -> [ 確定 ] ResolveAction
        ActionResolver.Run(self, _threat); // 判断 : どの武器モード ( WantMode )
        PositionResolver.Run(self, _threat); // 判断 : どの位置 ( 現状 Follow01 固定 )
        _weaponSwitched = WeaponSelector.ApplyMode(self, ActionResolver.WantMode);
        ActionResolver.ResolveAction(self);
        CombatDriver.ActionResolver = ActionResolver;
        // 切替を発火したフレームは交戦を1回休む ( settle )。移動は通常どおり
        //   ApplyMode が切替前に ReleaseFireIfPressed 済 = 暴発防止。かつ held 反映の 1 frame 遅延もここで吸収
    }

    #endregion

    #region 交戦に関するメソッド

    private static void RunCombatDriver(EntityPlayerLocal self)
    {
        // --- 交戦オーバーレイ（Section E）: 最後に実行 ---
        //   in-range の近接は 3D エイムで上の平面 facing を上書きしつつ press 駆動。
        if (!_weaponSwitched) CombatDriver.OnCombatStep(self, _threat);
    }

    #endregion

    #region ユーティリティの実行

    private static void RunUtilities(EntityPlayerLocal self)
    {
        // --- デバッグ・オーバーレイ ( 移動目的地の光柱 ) : world / leader 確定直後 ---
        //   真実を読むだけ ( leader.position と PathFollowState の公開配列 )
        //   移動ロジックは複製しない
        DebugOverlay.Sync(self, _leader);

        LeaderItemPickup.MaybeRun(self, _leader);
    }

    private static void RunRepositoryUtilities(EntityPlayerLocal self)
    {
        if (Cfg.Enabled)
        {
            WeaponSelector.RefreshLoadout(self, true);
            ItemStower.MaybeRun(self, true);
        }

        ItemStower.MaybeRun(self, false);
    }

    #endregion

    #region 移動に関するメソッド

    private static bool RunMeleePositioning(EntityPlayerLocal self)
    {
        // --- v0.8(B): 格闘オートアプローチ（follow より優先） ---
        //   格闘武器 かつ 交戦中脅威が「リーチ外 かつ approachMax 内」のとき、
        //   移動目標をリーダーから脅威へ差し替えて前進する（既存の Stop@standoff / Steer→_leader を上書き）。
        //   接近steer の後に交戦オーバーレイを回して、リーチに入った瞬間から歩きながら振れるようにする。
        if (!TryMeleeApproach(self, in _threat)) return false;
        if (!_weaponSwitched) CombatDriver.OnCombatStep(self, _threat);
        return true;
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

    private static void RunFollowPositioning(EntityPlayerLocal self)
    {
        // --- posture: follow ---
        var flat = _leader.position - self.position;
        flat.y = 0f;
        var dist = flat.magnitude;

        var lookDir = flat;
        if (Cfg.CombatMode && _threat.Valid)
        {
            var toThreat = _threat.Target.position - self.position;
            toThreat.y = 0f;
            if (toThreat.sqrMagnitude > 0.001f) lookDir = toThreat;
        }

        if (dist <= Cfg.StandoffMeters)
        {
            if (Cfg.CombatMode && _threat.Valid) FaceOnly(self, lookDir);
            Stop(self);
        }
        else
        {
            // 既定は直線でリーダーへ。経路が届いていれば中間ウェイポイントへ向かう（navigation スライス3）。
            var moveTarget = _leader.position;
            var pathActive = false;
            if (Cfg.PathFollow &&
                PathFollowState.TryGetMoveTarget(self.position, Cfg.WaypointArriveM, Cfg.WaypointHeightTolM,
                    Cfg.PathStaleSec, out var wpTarget, out var _pstatus))
            {
                moveTarget = wpTarget;
                pathActive = true;
            }

            // 戦闘中は脅威を向く(既存優先)。非戦闘の経路追従中のみ進行方向を向く。
            if (!(Cfg.CombatMode && _threat.Valid) && pathActive)
            {
                var tdir = moveTarget - self.position;
                tdir.y = 0f;
                if (tdir.sqrMagnitude > 0.001f) lookDir = tdir;
            }

            Steer(self, moveTarget, lookDir, dist > Cfg.RunMeters);
        }
    }

    private static void FaceOnly(EntityPlayerLocal self, Vector3 lookDir)
    {
        var ld = lookDir;
        ld.y = 0f;
        if (ld.sqrMagnitude > 0.001f) FaceWorldDir(self, ld.normalized);
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

        Logger.LogJump(wp, flat, legCell, legBlocked, headCell, headClear, jump);

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

    #endregion
}