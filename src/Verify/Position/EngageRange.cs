/*
 *
 * EngageRange.cs
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
using UnityEngine;
using Logger = CompanionAIVerify.Log.Logger;

namespace CompanionAIVerify.Position;

/// <summary>
///     Slice A: 現在保持中の武器から「交戦の実効レンジ」をランタイムで読み出す。
///     挙動は変えない（読み出しと観察ログのみ）。B(格闘リーチ/接近) / C(遠隔射程) / D(友軍ゲート) が共通で消費する。
///     ソース確定事項（decompiled 3.1.0）:
///     - 主攻撃アクションは Actions[0]。         Inventory.GetHoldingGun(): holdingItem.Actions[0] as ItemActionAttack
///     (Inventory.cs:737-739)
///     GetPrimaryAction(): holdingItem.Actions[0]                              (Inventory.cs:729)
///     - 遠隔/近接の判別も Actions[0] で行う。   vanilla: _itemValue.ItemClass.Actions[0] is ItemActionRanged
///     (ItemActionAttack.cs:1064)
///     - 遠隔の実効射程 = MaxRange 適用後。      ItemActionRanged.GetRange(): EffectManager.GetValue(MaxRange, .., Range)
///     (ItemActionRanged.cs:1376-1378)
///     - 近接のリーチ = Range フィールド。       melee hit は distanceSq > Range*Range で棄却
///     (ItemActionMelee.cs:148,228)
///     GetIdealAIRange() も return Range                                       (ItemActionAttack.cs:494-496)
///     - Range/BlockRange/SphereRadius は public（publicize 済み）。inventory は EntityAlive:764。
/// </summary>
internal static class EngageRange
{
    internal static readonly Info Invalid = new() { valid = false };
    private static readonly Dictionary<int, LogState> _last = new();

    /// <summary>
    ///     holder が今持っている武器の実効レンジを読む。素手・非攻撃アイテム・未初期化は valid=false。
    /// </summary>
    internal static Info Read(EntityAlive holder)
    {
        if (holder == null) return Invalid;

        var inv = holder.inventory; // EntityAlive.cs:764
        if (inv == null) return Invalid;

        var held = inv.holdingItem; // Inventory.cs:182
        var heldData = inv.holdingItemData; // Inventory.cs:208
        if (held == null || heldData == null) return Invalid;
        if (held.Actions == null || held.Actions.Length == 0) return Invalid;

        var a0 = held.Actions[0]; // 主攻撃アクション（Inventory.cs:729,737-739）
        if (a0 == null) return Invalid;

        ItemActionData adata = null;
        if (heldData.actionData != null && heldData.actionData.Count > 0)
            adata = heldData.actionData[0]; // ItemActionAttack.cs:1117,1131 と同経路

        var info = new Info
        {
            valid = true,
            sphereRadius = a0.SphereRadius, // ItemAction.cs:48（共通基底に public）
            actionType = a0.GetType().Name
        };

        // ── 攻撃アクションは2系統に分かれる。3.1.0 の継承（ソース確定）:
        //    ItemActionRanged      : ItemActionAttack : ItemAction   … 銃・遠隔
        //    ItemActionMelee       : ItemActionAttack : ItemAction   … 旧近接
        //    ItemActionDynamicMelee: ItemActionDynamic : ItemAction  … 現行近接/ツール（ItemActionAttack を継承しない！）
        //  旧実装は Actions[0] as ItemActionAttack だけを見ていたため、現行近接(Dynamic)が全て取りこぼされ INVALID になっていた。

        // 1) 遠隔: vanilla の判別と同じく ItemActionRanged 型で判定（ItemActionAttack.cs:1064）。
        var ranged = a0 as ItemActionRanged;
        if (ranged != null && adata != null)
        {
            info.isRanged = true;
            info.range = ranged.GetRange(adata); // MaxRange 適用後の発射射程（ItemActionRanged.cs:1376）
            info.blockRange = ranged.BlockRange; // 継承（ItemActionAttack.cs:159）
            return info;
        }

        // 2) 現行近接/ツール: ItemActionDynamicMelee : ItemActionDynamic。
        var dyn = a0 as ItemActionDynamic;
        if (dyn != null)
        {
            info.isRanged = false;
            // Range は攻撃実行時にのみ再計算される（ItemActionDynamic.cs:342）。非攻撃tickでは 0 のことがあるため
            // 常に安定して読める RangeDefault（XML "Range", 既定2f, ItemActionDynamic.cs:136-137）へフォールバック。
            info.range = dyn.Range > 0f ? dyn.Range : dyn.RangeDefault;
            info.blockRange = dyn.BlockRange > 0f ? dyn.BlockRange : info.range;
            return info;
        }

        // 3) 旧近接: ItemActionMelee : ItemActionAttack。
        var atk = a0 as ItemActionAttack;
        if (atk != null)
        {
            info.isRanged = false;
            info.range = atk.Range; // GetIdealAIRange と同値（ItemActionAttack.cs:494-496）
            info.blockRange = atk.BlockRange; // ItemActionAttack.cs:159
            return info;
        }

        // 4) いずれの攻撃アクションでもない（真の非武器）→ 安全側デフォルト
        return Invalid;
    }

    /// <summary>
    ///     holder→target の「レイ長に相当する距離」。
    ///     melee/ranged いずれもレイ原点は視点(eye)、当たりはチェスト付近なので eye→chest を採る。
    ///     eye:   GetLookRay().origin = position + (0, GetEyeHeight(), 0)   (EntityAlive.cs:5536-5538)
    ///     chest: getChestPosition()   （GetAttackTargetHitPosition が使用, EntityAlive.cs:5895-5898）
    /// </summary>
    internal static float EyeToChestDistance(EntityAlive holder, EntityAlive target)
    {
        if (holder == null || target == null) return float.PositiveInfinity;
        var eye = holder.GetLookRay().origin;
        var chest = target.getChestPosition();
        return Vector3.Distance(eye, chest);
    }

    /// <summary>
    ///     交戦tickで呼ぶ。target が null でも range 自体は出せる（target 距離は n/a 表示）。
    ///     何も変えない。数値が実プレイと合うかを目視確認するためだけのもの。
    /// </summary>
    internal static void LogTick(EntityAlive holder, EntityAlive target)
    {
        if (!Cfg.LogEngageRange || holder == null) return;

        var info = Read(holder);

        var d = target != null ? EyeToChestDistance(holder, target) : float.NaN;
        var inRange = info.valid && target != null && d <= info.range;

        // 変更検知キー（丸めてチラつきを抑える）
        var key = info.valid
            ? $"{(info.isRanged ? "RANGED" : "MELEE")}|R={info.range.ToString("F2")}|d={(float.IsNaN(d) ? "na" : d.ToString("F1"))}|in={(inRange ? "1" : "0")}"
            : "INVALID";

        var id = holder.entityId;
        var now = Time.time;
        if (_last.TryGetValue(id, out var st))
            if (st.key == key && now - st.t < Cfg.EngageLogMinInterval)
                return;
        _last[id] = new LogState { t = now, key = key };

        if (!info.valid)
        {
            Logger.LogRange(true, id, string.Empty, string.Empty, 0.0f, 0.0f, 0.0f, string.Empty, false);
            return;
        }

        Logger.LogRange(false, id, info.isRanged ? "RANGED" : "MELEE", info.actionType, info.range, info.blockRange,
            info.sphereRadius, float.IsNaN(d) ? "n/a" : d.ToString("F2"), inRange);
    }

    internal struct Info
    {
        public bool valid; // false のとき呼び出し側は「安全側デフォルト」(交戦しない/追従) を採る
        public bool isRanged; // true: 遠隔（range は発射実効射程）/ false: 近接（range はリーチ）
        public float range; // 交戦の主レンジ（遠隔=GetRange / 近接=Range）
        public float blockRange; // 参考（ブロック用リーチ）
        public float sphereRadius; // 近接の横方向許容（スフィアキャスト半径）
        public string actionType; // ログ用: アクションの型名（例 ItemActionDynamicMelee / ItemActionRanged）
    }

    // ---- 観察ログ（Slice A 専用。B 以降は Cfg.LogEngageRange=false で黙らせる想定） ----
    // 変更検知 + 時間ゲートで spam を抑える（mod の既存方針に合わせた最小実装）。holder ごとに前回値を保持。

    private struct LogState
    {
        public float t;
        public string key;
    }
}