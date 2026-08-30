using HarmonyLib;
using UnityEngine;

// =============================================================================
// Companion AI — locomotion + facing verification harness (7DTD 3.1.0)
// -----------------------------------------------------------------------------
// 目的: follow が閉じた次のスライスとして「体の向き（facing）」を実機確定する。
//       facing 駆動を SetRotation(Vector3) で行う版（検証パターン(1)）。
//
// ★この版の要点（SetRotation 経路）:
//   EntityPlayerLocal.SetRotation(Vector3 _rot) は _rot をオイラー角(度)で受け、
//     - base(Entity)  : rotation / qrotation を更新            (Entity:2581-2585)
//     - override(EPL) : PhysicsTransform.eulerAngles と
//                       m_vp_FPCamera.Angle = (-_rot.x, _rot.y) を同時更新 (EPL:2310-2321)
//   → 体(PhysicsTransform)とFPカメラを一括で回す。rotation フィールド直書きは
//     カメラが追従しないため不可。必ず SetRotation を通す。
//
//   「ある方向を向く」変換は game 本体の look-at と同一式を踏襲する:
//     euler = Quaternion.LookRotation(dir, Vector3.up).eulerAngles; euler.x *= -1f;
//     self.SetRotation(euler);                                     (EPL:248-252, 239)
//   ピッチ反転(.x*=-1)はカメラ側 (-_rot.x) 規約に合わせるため。XZ平坦dirなら x=0。
//
// 設計判断（据え置き）:
//   - look と move は分離のまま。executor は Steer(moveTarget, lookDir)。
//   - 既定(a): look = 進行方向。将来(b)「脅威を向いたまま strafe」は lookDir 差し替えのみ。
//   - facing は単一関数 FaceWorldDir() に隔離。検証パターン(2)（SetLookPosition 経路）へ
//     切り替えるときは、この1関数の中身を差し替えるだけで済むよう縫い目を残してある。
//
// 移動分解の変更点（SetRotation 版に伴う堅牢化）:
//   旧版は SetLookPosition 後に GetLookVector() を読み直して基底にしていたが、
//   本版は「意図した向き lookFwd」を解析的に基底へ使う。SetRotation の同フレーム反映有無に
//   依存せず forward/strafe を確定できる（回転APIの反映タイミングを検証対象から切り離す）。
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
            Log.Out("[CompanionAI] verify harness loaded (facing = SetRotation). F8 to toggle drive.");
        }
    }

    // --- Tunables ------------------------------------------------------------
    internal static class Cfg
    {
        internal static bool    Enabled        = false;           // 起動時OFF。F8でトグル
        internal const  KeyCode ToggleKey      = KeyCode.F8;
        internal const  float   StandoffMeters = 3.0f;            // これ以内なら停止
        internal const  float   RunMeters      = 8.0f;            // これ以上離れたら走る
        // 検証中は回転を毎フレーム直接セット（スナップ）。ヨーの追従補間は後段の課題。
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
            // (b) にするなら lookDir にだけ脅威座標(への方向)を渡す。executor はこの1行以外不変。
            Steer(self, moveTarget: leader.position, lookDir: flat, running: dist > Cfg.RunMeters);
        }

        // R_steer: 「どこへ動くか(moveTarget)」と「どこを向くか(lookDir)」を独立入力にする。
        // facing は SetRotation で駆動し、move は「意図した向き lookFwd」を基底に forward/strafe へ分解。
        private static void Steer(EntityPlayerLocal self, Vector3 moveTarget, Vector3 lookDir, bool running)
        {
            Vector3 toTarget = moveTarget - self.position; toTarget.y = 0f;
            if (toTarget.sqrMagnitude < 0.01f) { Stop(self); return; }
            Vector3 moveWorld = toTarget.normalized;

            // 1) facing を駆動。lookDir(XZ) が有効ならその方向へ体+カメラを回す。
            //    lookFwd は「意図した向き」を解析的に確定（GetLookVector の同フレーム反映に非依存）。
            Vector3 ld = lookDir; ld.y = 0f;
            Vector3 lookFwd;
            if (ld.sqrMagnitude > 0.001f)
            {
                lookFwd = ld.normalized;
                FaceWorldDir(self, lookFwd);          // ★ SetRotation 経路
            }
            else
            {
                lookFwd = moveWorld;                  // 向き指定なし → 進行方向を基底に使う（回転はしない）
            }

            // 2) look 前方(XZ)を基底に、目標移動方向を forward/strafe へ分解。
            Vector3 lookRight = Vector3.Cross(Vector3.up, lookFwd); // Unity左手系: = look-right(XZ)

            self.movementInput.moveForward = Mathf.Clamp(Vector3.Dot(moveWorld, lookFwd),  -1f, 1f);
            self.movementInput.moveStrafe  = Mathf.Clamp(Vector3.Dot(moveWorld, lookRight), -1f, 1f);
            self.movementInput.running     = running;
            self.movementInput.jump        = false;
            self.movementInput.down        = false;
        }

        // ---- facing (検証パターン(1): SetRotation 経路) -------------------------
        // 「この方向(worldDir)へ体を向ける」。game 本体の look-at と同一変換を踏襲:
        //   Quaternion.LookRotation(dir, up).eulerAngles → x を反転 → SetRotation。
        // SetRotation が rotation/qrotation/PhysicsTransform/FPカメラを一括更新する
        //   (EntityPlayerLocal:2310-2321, Entity:2581-2585 / 変換式 EPL:248-252)。
        //
        // 縫い目メモ: 検証パターン(2)（SetLookPosition 経路）へ切り替えるときは、
        //   この関数の中身だけを差し替える（呼び出し側 Steer は不変）。
        private static void FaceWorldDir(EntityPlayerLocal self, Vector3 worldDir)
        {
            if (worldDir.sqrMagnitude < 1e-6f) return;
            Vector3 euler = Quaternion.LookRotation(worldDir.normalized, Vector3.up).eulerAngles;
            euler.x *= -1f;                            // game 本体と同じピッチ反転（EPL:239, 251）
            self.SetRotation(euler);
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
