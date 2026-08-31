/*
 *
 * Patch_FreezeHostiles.cs
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
using CompanionAIVerify.Config;
using HarmonyLib;
using UnityEngine;

namespace CompanionAIVerify.Utility;

// =============================================================================
// テスト専用ハーネス: 敵対エンティティをその場に固定（交戦状態は維持したまま一歩も動かさない）。
//
//   目的: MeleeApproachMaxDistance の検証で、「コンパニオンから N m の位置に静止した交戦中ゾンビ」を
//         再現したい。通常はゾンビがリーダー/コンパニオンへ寄ってしまい、狙った距離関係を保てない。
//         固定できれば、5m/6m/7m…に置いて「approachMax=6.0 の内外で接近する/しない」を直接観察できる。
//
//   なぜ位置クランプか:
//     ゾンビはルートモーション（アニメ駆動）で移動する（EntityAlive:3987 DefaultMoveEntity /
//     accumulatedRootMotion→motion→SetPosition:3981）。moveSpeed を 0 にしても num3=landMovementFactor*2.5
//     で決まり止まらない（:3993-3994）。そこで毎tick、位置をアンカーへスナップし戻して translation を打ち消す。
//     回転・攻撃アニメは活きるので「起きて交戦している」状態は保たれる。
//
//   交戦成立との関係:
//     ThreatScanner は「起きている敵対」を Valid にする（IsSleeping の Unawakened のみ除外）。
//     ActionDriver/approach は State==Engaged に依存しない（State はログ表示のみ）。
//     → 固定されていてもコンパニオンは通常どおり脅威として検知・接近・交戦する。pin ハーネスは不要。
//
//   実装:
//     ・EntityAlive.OnUpdateEntity() の postfix（per-tick, 移動適用後）で位置をアンカーへ戻す。
//     ・アンカー = トグルON中に初めて見た時点の位置（entityId 毎に記録）。トグルOFFで全消去。
//     ・敵対のみ（EntityEnemy）。ホスト(サーバ)限定。
//     ・Cfg.DebugFreezeHostiles=false のとき先頭で return＝挙動ゼロ変化。ON中はワールド内の全敵対が固定
//       される点に注意（手動スポーン検証用途を想定）。PatchAll() で自動登録。
//
//   ※ 観察用スキャフォールドであり製品挙動ではない。検証後は false のまま。
//   ※ 平地でスポーンすること（アンカーは初回位置なので、落下中に捕捉すると空中固定になる）。
// =============================================================================
[HarmonyPatch(typeof(EntityAlive), "OnUpdateEntity")]
internal static class Patch_DebugFreezeHostiles
{
    // entityId -> アンカー位置
    private static readonly Dictionary<int, Vector3> _anchors = new();

    private static void Postfix(EntityAlive __instance)
    {
        if (!Cfg.DebugFreezeHostiles)
        {
            if (_anchors.Count > 0) _anchors.Clear(); // トグルOFFで固定解除
            return;
        }

        if (__instance == null || __instance.IsDead()) return;
        if (!(__instance is EntityEnemy)) return; // 敵対のみ（プレイヤー/受動MOBは固定しない）

        var cm = SingletonMonoBehaviour<ConnectionManager>.Instance;
        if (cm == null || !cm.IsServer) return; // ホスト限定

        var id = __instance.entityId;
        if (!_anchors.TryGetValue(id, out var anchor))
        {
            anchor = __instance.position; // 初回：現在位置をアンカー化
            _anchors[id] = anchor;
            return;
        }

        // ずれていればアンカーへスナップ（ルートモーション/nav を打ち消す）
        if ((__instance.position - anchor).sqrMagnitude > 0.0001f)
            __instance.SetPosition(anchor); // Entity:2543
    }
}