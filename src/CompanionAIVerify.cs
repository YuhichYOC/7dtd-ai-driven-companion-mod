/*
*
* CompanionAIVerify.cs
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

using HarmonyLib;
using UnityEngine;

// =============================================================================
// Companion AI — locomotion verification harness (7DTD 3.1.0)
// -----------------------------------------------------------------------------
// 目的: 「知覚 → executor → 移動」の一周を、最小スライス hold/follow で閉じる。
//
// 経路: 中間層のみ（movementInput の上書き）。攻撃/照準/TPカメラ・ゲートは未使用。
//   MoveByInput() が movementInput を消費する "前" に prefix で上書きする。
//   コンパニオンPCには人間入力が無い（=ゼロ）ので、上書きがそのまま駆動になる。
//
// 設計判断（確定済み）:
//   - look と move を最初から分離。executor は R_steer(moveTarget, lookDir) の二引数。
//   - 既定は (a) look = 進行方向。将来 (b)「脅威を向いたまま strafe」や文脈判断を
//     足すときは lookDir に渡す値を変えるだけで、この executor は無改造。
//
// 導入: COMPANION クライアントPCにのみ入れる。F8 で駆動ON/OFF（検証用トグル）。
// 参照DLL: Assembly-CSharp.dll / UnityEngine.CoreModule.dll / 0Harmony.dll
// =============================================================================

namespace CompanionAIVerify
{
    // --- Mod entry -----------------------------------------------------------
    public class CompanionAIVerifyModApi : IModApi
    {
        public void InitMod(Mod _modInstance)
        {
            var harmony = new Harmony("companionai.verify");
            harmony.PatchAll();
            Log.Out("[CompanionAI] verify harness loaded. F8 to toggle drive.");
        }
    }

    // --- Tunables ------------------------------------------------------------
    internal static class Cfg
    {
        internal static bool   Enabled       = false;            // 起動時OFF。F8でトグル
        internal const  KeyCode ToggleKey     = KeyCode.F8;
        internal const  float  StandoffMeters = 3.0f;            // これ以内なら停止
        internal const  float  RunMeters      = 8.0f;            // これ以上離れたら走る
        internal const  float  LookAheadMeters = 5.0f;           // SetLookPosition の前方距離
    }

    // --- Executor ------------------------------------------------------------
    // permitted_actions のうち hold / follow だけを実装した最小版。
    // 上位層(Layer0/1)やゲートウェイはまだ介在しない（follow を固定で駆動）。
    internal static class CompanionExecutor
    {
        // MoveByInput の prefix から毎フレーム呼ばれる。__instance は常にローカルプレイヤー
        // （MoveByInput は EntityPlayerLocal のみが回すため）。
        internal static void OnMovePrefix(EntityPlayerLocal self)
        {
            if (Input.GetKeyDown(Cfg.ToggleKey))
            {
                Cfg.Enabled = !Cfg.Enabled;
                Log.Out("[CompanionAI] drive = " + Cfg.Enabled);
                if (!Cfg.Enabled) Stop(self); // OFFにした瞬間に確実に止める
            }
            if (!Cfg.Enabled) return; // vanilla挙動に委ねる（=手動操作可能）

            // 安全ガード: 一応ローカル主プレイヤーのみ駆動
            var world = GameManager.Instance != null ? GameManager.Instance.World : null;
            if (world == null || self != world.GetPrimaryPlayer()) return;

            EntityPlayer leader = FindNearestLeader(world, self);
            if (leader == null) { Stop(self); return; }

            // --- posture: follow ---
            Vector3 flat = leader.position - self.position; flat.y = 0f;
            float dist = flat.magnitude;
            if (dist <= Cfg.StandoffMeters) { Stop(self); return; }

            // 既定(a): look = 進行方向（= leader 方向）。
            // (b) にするなら lookDir にだけ脅威座標を渡す。executor はこの1行以外不変。
            Steer(self, moveTarget: leader.position, lookDir: flat, running: dist > Cfg.RunMeters);
        }

        // R_steer: 「どこへ動くか(moveTarget)」と「どこを向くか(lookDir)」を独立入力にする。
        // move は look-yaw 基底へ射影して (moveForward, moveStrafe) に分解する。
        // → look と move が分離されるので、(b) や文脈判断は lookDir 差し替えだけで載る。
        private static void Steer(EntityPlayerLocal self, Vector3 moveTarget, Vector3 lookDir, bool running)
        {
            Vector3 toTarget = moveTarget - self.position; toTarget.y = 0f;
            if (toTarget.sqrMagnitude < 0.01f) { Stop(self); return; }
            Vector3 moveWorld = toTarget.normalized;

            // 1) look を駆動（SetLookPosition が実際に player の視線/体を回すかは要検証。
            //    回らない場合は movementInput.rotation へのデルタ注入に切替）。
            Vector3 ld = lookDir; ld.y = 0f;
            if (ld.sqrMagnitude > 0.001f)
                self.SetLookPosition(self.position + ld.normalized * Cfg.LookAheadMeters);

            // 2) 現在の look 前方(XZ)を基底に、目標移動方向を forward/strafe へ分解。
            Vector3 lookFwd = self.GetLookVector(); lookFwd.y = 0f;
            if (lookFwd.sqrMagnitude < 0.001f) lookFwd = moveWorld; else lookFwd.Normalize();
            Vector3 lookRight = Vector3.Cross(Vector3.up, lookFwd); // Unity左手系: = look-right(XZ)

            self.movementInput.moveForward = Mathf.Clamp(Vector3.Dot(moveWorld, lookFwd),   -1f, 1f);
            self.movementInput.moveStrafe  = Mathf.Clamp(Vector3.Dot(moveWorld, lookRight), -1f, 1f);
            self.movementInput.running     = running;
            self.movementInput.jump        = false;
            self.movementInput.down        = false;
        }

        // R_stop: 安全既定。移動入力をゼロに。
        private static void Stop(EntityPlayerLocal self)
        {
            self.movementInput.moveForward = 0f;
            self.movementInput.moveStrafe  = 0f;
            self.movementInput.running     = false;
            self.movementInput.jump        = false;
            self.movementInput.down        = false;
        }

        // 検証用のリーダー解決: 自分以外で最も近い生存プレイヤー。
        // （本番では認可リスト＝hire対象を参照。ここでは最短距離で代用）
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
    }

    // --- Harmony patch -------------------------------------------------------
    // MoveByInput が movementInput を moveDirection へ落とす直前に割り込む。
    // prefix は void（元メソッドはそのまま実行される）。
    [HarmonyPatch(typeof(EntityPlayerLocal), "MoveByInput")]
    internal static class Patch_EntityPlayerLocal_MoveByInput
    {
        private static void Prefix(EntityPlayerLocal __instance)
        {
            CompanionExecutor.OnMovePrefix(__instance);
        }
    }
}
