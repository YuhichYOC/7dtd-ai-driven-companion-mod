/*
 *
 * WeaponSelector.cs
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

using CompanionAIVerify.Combat;
using CompanionAIVerify.Config;
using UnityEngine;
using Logger = CompanionAIVerify.Log.Logger;

namespace CompanionAIVerify.ToolSelection;

// --- (A) 武器自動切替 -----------------------------------------------------
//   ツールベルトの「最初の銃スロット / 最初の近接スロット」をキャッシュし、
//   脅威距離に応じてヒステリシスで持ち替える。持ち替えは NetPackageHoldingItem 経由で
//   サーバへ同期される（EntityAlive:3082 ForceHoldingWeaponUpdate）。
internal static class WeaponSelector
{
    private static int _rangedSlot = -1;
    private static int _meleeSlot = -1;
    private static float _nextScan;
    private static float _nextSwitchTime;

    internal static bool HasRanged => _rangedSlot >= 0;
    internal static bool HasMelee => _meleeSlot >= 0;

    // 保持中アイテムのモード。分類器で判定（松明等の除外・工具の包含が反映される）。
    internal static WeaponMode CurrentHeldMode(EntityPlayerLocal self)
    {
        var inv = self.inventory;
        var hi = inv != null ? inv.holdingItem : null;
        return WeaponClassifier.Classify(hi);
    }

    // ツールベルト走査（throttled）。分類器で最初の銃/近接スロットを記録。
    internal static void RefreshLoadout(EntityPlayerLocal self, bool force)
    {
        if (!force && Time.time < _nextScan) return;
        _nextScan = Time.time + Cfg.LoadoutScanIntervalSec;

        if (force) WeaponClassifier.Rebuild(); // config 反映（F8）はここで拾う。以外は Classify が遅延構築

        _rangedSlot = -1;
        _meleeSlot = -1;
        var inv = self.inventory;
        if (inv == null) return;

        var n = inv.PUBLIC_SLOTS; // 再生モードで 10
        for (var i = 0; i < n; i++)
        {
            var st = inv.GetItem(i);
            if (st == null || st.IsEmpty()) continue;
            switch (WeaponClassifier.Classify(st.itemValue.ItemClass))
            {
                case WeaponMode.Ranged:
                    if (_rangedSlot < 0) _rangedSlot = i;
                    break;
                case WeaponMode.Melee:
                    if (_meleeSlot < 0) _meleeSlot = i;
                    break;
            }
        }
    }

    // ActionResolver が決めた希望モードへ持ち替える ( 実行のみ )
    // 実際に切替を発火したら true ( settle 対象 )
    //   AutoWeaponSwitch OFF / throttle 中 / 既に希望モード保持 / 無効スロット は false
    //   切替前にトリガー / ドローを安全開放する ( 弓ドロー中の暴発防止 )
    internal static bool ApplyMode(EntityPlayerLocal self, WeaponMode mode)
    {
        if (!Cfg.AutoWeaponSwitch || mode == WeaponMode.None) return false;

        var inv = self.inventory;
        if (inv == null) return false;
        if (CurrentHeldMode(self) == mode) return false; // 既に希望モードを保持

        var slot = mode == WeaponMode.Ranged ? _rangedSlot : _meleeSlot;
        if (slot < 0 || slot == inv.holdingItemIdx) return false;
        if (Time.time < _nextSwitchTime) return false;
        _nextSwitchTime = Time.time + Cfg.WeaponSwitchMinIntervalSec;

        CombatDriver.ReleaseFireIfPressed(self);
        inv.SetHoldingItemIdxNoHolsterTime(slot);

        Logger.LogWeaponSwitch(mode == WeaponMode.Ranged ? "RANGED" : "MELEE", slot, -1.0f, _rangedSlot,
            _meleeSlot); // d は ActionResolver 側の判断ログへ移す想定で -1
        return true;
    }
}