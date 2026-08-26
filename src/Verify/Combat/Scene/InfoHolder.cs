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

        private float _fireMax;
        internal float FireMax { set => _fireMax = value; get => _fireMax; }

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

        private ADSOperation _adsOperation;
        internal ADSOperation ADSOperation { get => _adsOperation; }

        private AimOperation _aimOperation;
        internal AimOperation AimOperation { get => _aimOperation; }

        private FaceOperation _faceOperation;
        internal FaceOperation FaceOperation { get => _faceOperation; }

        private ReleaseOperation _releaseOperation;
        internal ReleaseOperation ReleaseOperation { get => _releaseOperation; }

        private SwingOperation _swingOperation;
        internal SwingOperation SwingOperation { get => _swingOperation; }

        private SwitchOperation _switchOperation;
        internal SwitchOperation SwitchOperation { get => _switchOperation; }

        private ADSActionValidator _adsActionValidator;
        internal ADSActionValidator ADSActionValidator { get => _adsActionValidator; }

        private ApprovementValidator _approvementValidator;
        internal ApprovementValidator ApprovementValidator { get => _approvementValidator; }

        private ReachValidator _reachValidator;
        internal ReachValidator ReachValidator { get => _reachValidator; }

        internal Info(EntityPlayerLocal s, ThreatInfo t, bool r, LogInfoHolder li)
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
            _adsOperation = new ADSOperation(this, li);
            _aimOperation = new AimOperation(this, li);
            _faceOperation = new FaceOperation(this, li);
            _releaseOperation = new ReleaseOperation(this, li);
            _swingOperation = new SwingOperation(this, li);
            _switchOperation = new SwitchOperation(this, li);
            _adsActionValidator = new ADSActionValidator(this, li);
            _approvementValidator = new ApprovementValidator(this, li);
            _reachValidator = new ReachValidator(this, li);
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

        // 保持中アイテムの装填残弾 ( Meta )
        // 取得不可は -1
        // A4 : holdingItemItemValue.Meta
        internal int GetHoldingMeta()
        {
            var inv = _self.inventory;
            var iv  = inv != null ? inv.holdingItemItemValue : null;
            return iv != null ? iv.Meta : -1;
        }

#endregion -- 外から呼び出す可能性のある属性 --
    }
}
