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
using CompanionAIVerify.Action;
using CompanionAIVerify.Config;
using CompanionAIVerify.Perception;
using CompanionAIVerify.Position;
using CompanionAIVerify.Stance;
using CompanionAIVerify.ToolSelection;
using CompanionAIVerify.Utility;
using CompanionAIVerify.Utility.Debugging;
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

        RunCombatDriver(self);
        RunPositionDriver(self);
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
        ActionDriver.ReleaseFireIfPressed(self);
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
        PositionResolver.Run(self, _threat); // 判断 : どの位置

        _weaponSwitched = WeaponSelector.ApplyMode(self, ActionResolver.WantMode);

        ActionResolver.ResolveAction(self);
        ActionDriver.ActionResolver = ActionResolver;
        PositionDriver.PositionResolver = PositionResolver;
        // 切替を発火したフレームは交戦を1回休む ( settle )。移動は通常どおり
        //   ApplyMode が切替前に ReleaseFireIfPressed 済 = 暴発防止。かつ held 反映の 1 frame 遅延もここで吸収
    }

    #endregion

    #region 交戦に関するメソッド

    private static void RunCombatDriver(EntityPlayerLocal self)
    {
        // --- 交戦オーバーレイ（Section E）: 最後に実行 ---
        //   in-range の近接は 3D エイムで上の平面 facing を上書きしつつ press 駆動。
        if (!_weaponSwitched) ActionDriver.OnCombatStep(self, _threat);
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

    private static void RunPositionDriver(EntityPlayerLocal self)
    {
        PositionDriver.OnTick(self, _leader, _threat);
    }

    private static void Stop(EntityPlayerLocal self)
    {
        self.movementInput.moveForward = 0f;
        self.movementInput.moveStrafe = 0f;
        self.movementInput.running = false;
        self.movementInput.jump = false;
        self.movementInput.down = false;
    }

    #endregion
}