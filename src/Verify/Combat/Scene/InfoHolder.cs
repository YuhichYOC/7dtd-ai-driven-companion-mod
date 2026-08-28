/*
 *
 * InfoHolder.cs
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
using CompanionAIVerify.Combat.Operate;
using CompanionAIVerify.Combat.Validate;
using CompanionAIVerify.Config;
using CompanionAIVerify.Perception;
using CompanionAIVerify.Positioning;
using UnityEngine;

namespace CompanionAIVerify.Combat.Scene;

internal class InfoHolder
{
    // CombatDriver の持つ状態があまりに多すぎるので状態を Info にまとめる
    // 状態管理・状態取得に専念する
    //
    // Operation, Validator クラスも同居させる ... これは後でホルダーとなるクラスに分離する必要がある
    //   この MOD の各クラスインスタンスは 7dtd インスタンス一つに対してそれぞれ一つ存在すればいい
    //   なので各クラスインスタンスを最大一つしか保持できないコードのままにしてある。呼び出しごとにヒープを使いたくない

    private ThreatInfo _target;

    internal InfoHolder()
    {
        AdsOperation = new AdsOperation(this);
        AimOperation = new AimOperation(this);
        DrawOperation = new DrawOperation(this);
        FaceOperation = new FaceOperation(this);
        FpvOperation = new FpvOperation(this);
        ReleaseOperation = new ReleaseOperation(this);
        SwingOperation = new SwingOperation(this);
        SwitchOperation = new SwitchOperation(this);
        TriggerOperation = new TriggerOperation(this);
        AdsActionValidator = new AdsActionValidator(this);
        ApprovementValidator = new ApprovementValidator(this);
        FullAutoValidator = new FullAutoValidator(this);
        ReachValidator = new ReachValidator(this);
        ShootableValidator = new ShootableValidator(this);
        TargetValidator = new TargetValidator(this);
    }

    internal EntityPlayerLocal Self { get; set; }

    internal ThreatInfo Target
    {
        get => _target;
        set
        {
            _target = value;
            Reach = GetAttackReach();
            Distance = Mathf.Sqrt(_target.DistSq);
        }
    }

    internal bool Ranged { get; set; }

    internal float Reach { get; private set; }

    internal float Distance { get; private set; }

    internal float FireMax { get; set; }

    internal AdsOperation AdsOperation { get; }

    internal AimOperation AimOperation { get; }

    internal DrawOperation DrawOperation { get; }

    internal FaceOperation FaceOperation { get; }

    internal FpvOperation FpvOperation { get; }

    internal ReleaseOperation ReleaseOperation { get; }

    internal SwingOperation SwingOperation { get; }

    internal SwitchOperation SwitchOperation { get; }

    internal TriggerOperation TriggerOperation { get; }

    internal AdsActionValidator AdsActionValidator { get; }

    internal ApprovementValidator ApprovementValidator { get; }

    internal FullAutoValidator FullAutoValidator { get; }

    internal ReachValidator ReachValidator { get; }

    internal ShootableValidator ShootableValidator { get; }

    internal TargetValidator TargetValidator { get; }

    // 保持アイテムの実効リーチ
    // EngageRange.Read が Dynamic melee ( = ItemActionDynamic.Range / RangeDefault ) を正しく解決する
    // 旧実装は基底 ItemAction.Range を読んでいたため、Dynamic melee のリーチを取れず
    // 2.4m 武器を 2.0m フォールバック扱いしていた
    // 実ログで range=2.4 と確認済み
    // 取れなければ 2.0m
    private float GetAttackReach()
    {
        var er = EngageRange.Read(Self);
        if (er.valid && er.range > 0.01f) return er.range;
        return 2.0f;
    }

    #region -- 外から呼び出す可能性のある属性 --

    internal bool CombatActivated()
    {
        return Cfg.CombatMode && _target.Valid;
    }

    // ★ [bow] 保持中アイテムが弓 / クロスボウ ( ItemActionCatapult ) なら action と data を返す
    //   違えば null
    //   継承 : ItemActionCatapult : ItemActionLauncher : ItemActionRanged ( Launcher.cs:7 で確認 )
    //   ItemActionDataCatapult は publicize 済みで外部参照可 ( m_bActivated / m_ActivateTime / m_MaxStrainTime は public フィールド )
    internal Tuple<ItemActionCatapult, ItemActionCatapult.ItemActionDataCatapult> GetHeldCatapult()
    {
        var inv = Self.inventory;

        var hi = inv != null ? inv.holdingItem : null;
        if (hi == null || hi.Actions == null || hi.Actions.Length == 0) return null;

        var cat = hi.Actions[0] as ItemActionCatapult;
        if (cat == null) return null;

        var hid = inv != null ? inv.holdingItemData : null;
        if (hid == null || hid.actionData == null || hid.actionData.Count == 0) return null;

        return new Tuple<ItemActionCatapult, ItemActionCatapult.ItemActionDataCatapult>(cat,
            hid.actionData[0] as ItemActionCatapult.ItemActionDataCatapult);
    }

    // 保持中アイテムの装填残弾 ( Meta )
    // 取得不可は -1
    // A4 : holdingItemItemValue.Meta
    internal int GetHoldingMeta()
    {
        var inv = Self.inventory;
        var iv = inv != null ? inv.holdingItemItemValue : null;
        return iv != null ? iv.Meta : -1;
    }

    #endregion -- 外から呼び出す可能性のある属性 --
}