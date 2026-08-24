/*
*
* Info.cs
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

namespace CompanionAIVerify.Combat.Scene
{
    internal class InfoHolder
    {
        // CombatDriver の持つ状態があまりに多すぎるので状態を Info にまとめる
        // 状態管理・状態取得に専念する
        //
        // Operation, Validator クラスも同居させる ... これは後でホルダーとなるクラスに分離する必要がある
        //   この MOD の各クラスインスタンスは 7dtd インスタンス一つに対してそれぞれ一つ存在すればいい
        //   なので各クラスインスタンスを最大一つしか保持できないコードのままにしてある。呼び出しごとにヒープを使いたくない

        private EntityPlayerLocal _self;
        internal EntityPlayerLocal Self { get => _self; }

        private ThreatInfo _target;
        internal ThreatInfo Target { get => _target; }

        private bool _ranged;
        internal bool Ranged { get => _ranged; }

        private float _reach;
        internal float Reach { get => _reach; }

        private float _distance;
        internal float Distance { get => _distance; }

        private bool _targetInReach;
        internal bool TargetInReach { set => _targetInReach = value; get => _targetInReach; }

        private bool _attackPressed;
        internal bool AttackPressed { set => _attackPressed = value; get => _attackPressed; }

        private bool _aimAssistSet;
        internal bool AimAssistSet { set => _aimAssistSet = value; get => _aimAssistSet; }

        private bool _firePressed;
        internal bool FirePressed { set => _firePressed = value; get => _firePressed; }

        private bool _bowDrawing;
        internal bool BowDrawing { set => _bowDrawing = value; get => _bowDrawing; }

        private bool _adsOn;
        internal bool AdsOn { set => _adsOn = value; get => _adsOn; }

        private ReachValidator _reach;
        internal ReachValidator ReachValidator { get => _reach; }

        private ADSOperation _ads;
        internal ADSOperation ADSOperation { get => _ads; }

        private AimOperation _aim;
        internal AimOperation AimOperation { get => _aim; }

        private ReleaseOperation _release;
        internal ReleaseOperation ReleaseOperation { get => _release; }

        private SwitchOperation _switch;
        internal SwitchOperation SwitchOperation { get => _switch; }

        internal Info(EntityPlayerLocal s, ThreatInfo t, bool r)
        {
            _self = s;
            _target = t;
            _ranged = r;
            _reach = GetAttackReach(_self);
            _distance = Mathf.Sqrt(_target.DistSq);
            _attackPressed = false;
            _aimAssistSet = false;
            _firePressed = false;
            _bowDrawing = false;
            _adsOn = false;
            _reach = new ReachValidator(this);
            _ads = new ADSOperation(this);
            _aim = new AimOperation(this);
            _release = new ReleaseOperation(this);
            _switch = new SwitchOperation(this);
        }

        // 保持アイテムの実効リーチ
        // EngageRange.Read が Dynamic melee ( = ItemActionDynamic.Range / RangeDefault ) を正しく解決する
        // 旧実装は基底 ItemAction.Range を読んでいたため、Dynamic melee のリーチを取れず
        // 2.4m 武器を 2.0m フォールバック扱いしていた
        // 実ログで range=2.4 と確認済み
        // 取れなければ 2.0m
        private float GetAttackReach()
        {
            EngageRange.Info er = EngageRange.Read(_self);
            if (er.valid && er.range > 0.01f) return er.range;
            return 2.0f;
        }

#region -- 外から呼び出す可能性のある属性 --

        internal bool CombatActivated() => Cfg.CombatMode && _target.Valid;

        // ★ [bow] 保持中アイテムが弓/クロスボウ(ItemActionCatapult)なら action と data を返す
        //   違えば null
        //   継承 : ItemActionCatapult : ItemActionLauncher : ItemActionRanged ( Launcher.cs:7 で確認 )
        //   ItemActionDataCatapult は publicize 済みで外部参照可 ( m_bActivated / m_ActivateTime / m_MaxStrainTime は public フィールド )
        internal Tuple<ItemActionCatapult, ItemActionCatapult.ItemActionDataCatapult> GetHeldCatapult()
        {
            var inv = _self.inventory;

            var hi = inv != null ? inv.holdingItem : null;
            if (hi == null || hi.Actions == null || hi.Actions.Length == 0) return null;

            var cat = hi.Actions[0] as ItemActionCatapult;
            if (cat == null) return null;

            var hid = inv != null ? inv.holdingItemData : null;
            if (hid == null || hid.actionData == null || hid.actionData.Count == 0) return null;

            return new Tuple<ItemActionCatapult, ItemActionCatapult.ItemActionDataCatapult>(cat, hid.actionData[0] as ItemActionCatapult.ItemActionDataCatapult);
        }

        // ADS 可否
        // secondary action ( Actions[1] ) と actionData[1] が存在すること
        // AimingGun setter が actionData[1] を直接参照 = 境界外で例外になるためのガード
        internal bool CanAimDownSights()
        {
            var inv = _self.inventory;
            var hi  = inv != null ? inv.holdingItem : null;
            var hid = inv != null ? inv.holdingItemData : null;
            return hi != null && hi.Actions != null && hi.Actions.Length >= 2 && 
                hi.Actions[1] != null && hid != null && hid.actionData != null &&
                hid.actionData.Count >= 2;
        }

#endregion -- 外から呼び出す可能性のある属性 --
    }
}
