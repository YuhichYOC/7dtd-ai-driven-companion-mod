/*
 *
 * ActionResolver.cs
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

using System;
using CompanionAIVerify.Combat;
using CompanionAIVerify.Perception;
using CompanionAIVerify.ToolSelection;
using UnityEngine;

namespace CompanionAIVerify.Stance;

/*
 * 交戦への関わり方を解決する
 * ここで決めた値から使用ツール選択 & 交戦アクションの分岐へ進む
 *
 * 例 1
 * 遠距離からリーダーを弓で援護
 * 使用ツール選択 = <クラス未作成> ... 弓を選択
 * 交戦アクション = DrawPatternXX
 *
 * 例 2
 * リーダーへ集中しているゾンビの陽動を行う
 * 使用ツール選択 = <クラス未作成> ... 格闘武器を選択
 * 交戦アクション = MeleePatternXX
 *
 * 例 3
 * ブラッドムーンホード中であるため接近は控えて射撃で掃討
 * 使用ツール選択 = <クラス未作成> ... 銃を選択
 * 交戦アクション = TriggerActionXX
 *
 * このクラスで n8n ワークフロー実行結果を受け取り、遅い判断による挙動調整を行う
 * n8n を介さない早い判断もこのクラスを通る
 * 位置調整 ( PositionResolver ) はツール選択・アクションと並列で実行できるのでクラスを分ける
 */
internal class ActionResolver
{
    internal ActionResolver()
    {
        Action = Actions.None;
    }

    internal Actions Action { get; private set; }

    internal void Run(EntityPlayerLocal self, in ThreatInfo threat)
    {
        // 仮実装 ... 現時点ではターゲットとの距離で判定を行うステップのみ用意
        // CombatDriver.cs v0.7(A) から移植
        // 交戦距離に応じた武器自動切替
        WeaponSelector.RefreshLoadout(self, false);
        if (WeaponSelector.MaybeSwitch(self, Mathf.Sqrt(threat.DistSq))) CombatDriver.ReleaseFireIfPressed(self);

        Action = ClassifyHeld(self) switch
        {
            HeldKinds.None => Actions.None,
            HeldKinds.Bow => Actions.Draw01,
            HeldKinds.Melee => Actions.Melee01,
            HeldKinds.Crossbow => Actions.Trigger01,
            HeldKinds.Gun => Actions.Trigger01,
            _ => Actions.None
        };
    }

    private HeldKinds ClassifyHeld(EntityPlayerLocal self)
    {
        var inv = self != null ? self.inventory : null;
        var hi = inv?.holdingItem;
        if (hi?.Actions == null || hi.Actions.Length == 0) return HeldKinds.None;

        var a = hi.Actions[0];
        return a switch
        {
            ItemActionCatapult => IsCrossbow(hi) ? HeldKinds.Crossbow : HeldKinds.Bow,
            ItemActionRanged => IsCrossbow(hi) ? HeldKinds.Crossbow : HeldKinds.Gun,
            ItemActionMelee or ItemActionDynamicMelee => HeldKinds.Melee,
            _ => HeldKinds.None
        };
    }

    private bool IsCrossbow(ItemClass ic)
    {
        return (ic.Name ?? string.Empty).IndexOf("crossbow", StringComparison.Ordinal) >= 0;
    }

    internal enum Actions
    {
        None,
        Draw01,
        Melee01,
        Trigger01
    }

    private enum HeldKinds
    {
        None,
        Bow,
        Melee,
        Crossbow,
        Gun
    }
}