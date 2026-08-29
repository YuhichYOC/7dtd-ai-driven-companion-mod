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

using CompanionAIVerify.Config;
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
    // 距離ヒステリシスのラッチ
    // 両手武器保持時、デッドバンド内でモード維持
    // 判断状態は resolver が持つ
    private WeaponMode _modeLatch = WeaponMode.None;

    internal ActionResolver()
    {
        Action = Actions.None;
    }

    internal Actions Action { get; private set; }

    // WeaponSelector が持ち替える先の希望モード
    //   この resolver の「武器の決定」
    internal WeaponMode WantMode { get; private set; }

    // フェーズ 1
    //   どの武器モードで交戦するか決定 ( 在庫は変更しない )
    //   早い判断と n8n 意図の差し込み口になる
    internal void Run(EntityPlayerLocal self, in ThreatInfo threat)
    {
        WantMode = DecideMode(Mathf.Sqrt(threat.DistSq));
        // TODO ( 遅い判断 )
        // n8n の戻り値を受けて WantMode の調整を行うステップの追加
    }

    // フェーズ 2
    //   交戦アクションを確定
    //   WeaponSelector 実行の後に呼ぶ
    //   実際に保持中の武器型からバインドするため、切替の可否・1 frame 遅延に関わらず held と必ず一致する
    internal void ResolveAction(EntityPlayerLocal self)
    {
        Action = ClassifyHeld(self) switch
        {
            HeldKinds.None => Actions.None,
            HeldKinds.Bow => Actions.Draw01,
            HeldKinds.Melee => Actions.Melee01,
            HeldKinds.Launcher => Actions.Launcher01,
            HeldKinds.Gun => Actions.Trigger01,
            _ => Actions.None
        };
    }

    // 仮実装
    // 距離から希望モードへの変換
    // 両手武器のときだけヒステリシス
    // capability は WeaponSelector から読むだけ
    // 何を使うかの判断の所在はここ
    private WeaponMode DecideMode(float d)
    {
        var haveR = WeaponSelector.HasRanged;
        var haveM = WeaponSelector.HasMelee;
        if (!haveR && !haveM) return _modeLatch = WeaponMode.None;
        if (haveR ^ haveM) return _modeLatch = haveR ? WeaponMode.Ranged : WeaponMode.Melee;

        WeaponMode want;
        if (d <= Cfg.SwitchToMeleeMeters) want = WeaponMode.Melee;
        else if (d >= Cfg.SwitchToRangedMeters) want = WeaponMode.Ranged;
        else
            want = _modeLatch != WeaponMode.None
                ? _modeLatch
                : d <= (Cfg.SwitchToMeleeMeters + Cfg.SwitchToRangedMeters) * 0.5f
                    ? WeaponMode.Melee
                    : WeaponMode.Ranged;
        return _modeLatch = want;
    }

    private HeldKinds ClassifyHeld(EntityPlayerLocal self)
    {
        var inv = self != null ? self.inventory : null;
        var hi = inv?.holdingItem;
        if (hi?.Actions == null || hi.Actions.Length == 0) return HeldKinds.None;

        var a = hi.Actions[0];
        return a switch
        {
            ItemActionCatapult => HeldKinds.Bow,
            ItemActionLauncher => HeldKinds.Launcher,
            ItemActionRanged => HeldKinds.Gun,
            ItemActionMelee or ItemActionDynamicMelee => HeldKinds.Melee,
            _ => HeldKinds.None
        };
    }

    internal enum Actions
    {
        None,
        Draw01,
        Melee01,
        Launcher01,
        Trigger01
    }

    private enum HeldKinds
    {
        None,
        Bow,
        Melee,
        Launcher,
        Gun
    }
}