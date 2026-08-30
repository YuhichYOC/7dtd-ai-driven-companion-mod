/*
 *
 * Patch_FireOriginFix.cs
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

using CompanionAIVerify.Config;
using HarmonyLib;
using UnityEngine;

namespace CompanionAIVerify.Utility;

// =============================================================================
// 発砲時の攻撃レイ原点補正 (b案: 発砲経路限定スコープ)
//
//   問題: EntityPlayerLocal.GetLookRay()(EPL:3847) は playerCamera のレンズ位置を
//         そのまま射撃原点に採用する。一人称カメラ(vp_FPCamera)はピッチが深いほど
//         レンズを目(≈GetEyeHeight)から離す(俯角=下・後方 / 仰角=下・前方, 実測 約1.5m)。
//         一方 FaceOperation は eye=position+1.5 前提でピッチを算出するため、
//         急角度ほど「目基準のピッチ」を膝下・体外のレンズから撃つ形になり全弾外す
//         (実測: 急俯角76/76・急仰角307/320 が hit=none。命中は dir.y≈0 のときのみ)。
//
//   方針: 方向(dir)は正しい(常にターゲットへ向いている)。原点だけを目へ戻す。
//         ゲームの ItemActionRanged はヒットスキャン(fireShot→GetLookRay, 1426)も
//         弾体(GetActionEffectsValues→GetLookRay, ItemActionLauncher:209)も
//         TryExecuteAction の実行中に GetLookRay を呼ぶ。よって TryExecuteAction の
//         プレフィクス〜ファイナライザ間だけ補正窓を開き、その窓内の GetLookRay のみ
//         原点を position+up*GetEyeHeight() に差し替える(方向は保持)。
//         → 近接(GetMeleeRay)・インタラクト・レーザーサイト(GetExecuteActionTarget,
//           TryExecuteAction 外)・診断 ray-probe(Attack の外) には一切干渉しない。
//
//   スコープ/安全性:
//     ・Depth は無条件で加減算(収支保証)。補正は Cfg.Enabled のときだけ(=コンパニオンPC限定,
//       ホストは Enabled=false のまま=無変化)。
//     ・水平ショットは元々原点が目(実測 originYtoFeet=1.60=GetEyeHeight)なので無変化。
//     ・原点式は base EntityAlive.GetLookRay(EntityAlive:5538)と同一。crouch で目高が
//       変わるため固定1.5でなく GetEyeHeight() を使う。
//     ・PatchAll() により自動登録(CompanionAIVerify.cs)。
//
//   検証: この補正は Attack 内の TryExecuteAction 中のみ効くため、Attack の外で走る
//         診断 ray-probe の originYtoFeet/behind は補正後も変わらない(素のカメラ変位を
//         測るログのまま)。成否は hit=TARGET 率で判定すること。
// =============================================================================
internal static class FireOriginWindow
{
    internal static int Depth;
}

[HarmonyPatch(typeof(ItemActionRanged), "TryExecuteAction", new[] { typeof(ItemActionData) })]
internal static class Patch_FireOriginWindow
{
    private static void Prefix()
    {
        FireOriginWindow.Depth++;
    }

    private static void Finalizer()
    {
        FireOriginWindow.Depth--;
    }
}

[HarmonyPatch(typeof(EntityPlayerLocal), nameof(EntityPlayerLocal.GetLookRay))]
internal static class Patch_CompanionFireLookRayOrigin
{
    private static void Postfix(EntityPlayerLocal __instance, ref Ray __result)
    {
        if (!Cfg.Enabled || FireOriginWindow.Depth <= 0) return;

        // 方向は正しいので保持。原点だけ目へ戻す(レンズ変位を打ち消す)。
        __result = new Ray(
            __instance.position + Vector3.up * __instance.GetEyeHeight(),
            __result.direction);
    }
}