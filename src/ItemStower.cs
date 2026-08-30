/*
*
* ItemStower.cs
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

using UnityEngine;

namespace CompanionAIVerify
{
    // --- (B) ツールベルト優先配置 --------------------------------------------
    //   bag を走査し、武器(銃/近接)をツールベルトへ移送。throttled。
    //   注意: dynamic melee は斧・つるはし等の「工具」も含む。武器/工具の厳密区別は
    //   ItemTags 精査を要する follow-up（現状は Actions[0] 型分類で近似）。
    // v0.7.1
    //   分類器が Melee/Ranged と判定したものだけ昇格。松明は除外リストで落ちる。
    //   ※ ツールベルト満杯時の空け（退避）は未実装（ユーザー了承済み・後日）。
    internal static class ItemStower
    {
        private static float _nextRun;

        internal static void MaybeRun(EntityPlayerLocal self, bool force)
        {
            if (!Cfg.AutoStowWeaponsToToolbelt) return;
            if (!force && Time.time < _nextRun) return;
            _nextRun = Time.time + Cfg.ToolbeltStowIntervalSec;
            Run(self);
        }

        private static void Run(EntityPlayerLocal self)
        {
            Bag bag       = self.bag;
            Inventory inv = self.inventory;
            if (bag == null || inv == null) return;

            ItemStack[] bslots = bag.GetSlots();
            if (bslots == null) return;

            int moved = 0;
            for (int i = 0; i < bslots.Length; i++)
            {
                ItemStack st = bslots[i];
                if (st == null || st.IsEmpty()) continue;
                if (!IsWeaponStack(st)) continue;
                if (!inv.CanTakeItem(st)) continue; // ツールベルト空き無し（退避は後日）

                if (inv.AddItem(st.Clone(), out int slot) && slot >= 0)
                {
                    bag.SetSlot(i, ItemStack.Empty, true);
                    moved++;
                    Log.Out($"[CompanionAI] stow: bag[{i}] -> toolbelt[{slot}] {DescribeStack(st)}");
                }
            }
            if (moved > 0)
                Log.Out($"[CompanionAI] stow: moved {moved} weapon stack(s) to toolbelt.");
        }

        internal static bool IsWeaponStack(ItemStack st)
        {
            return WeaponClassifier.Classify(st.itemValue.ItemClass) != WeaponMode.None;
        }

        private static string DescribeStack(ItemStack st)
        {
            ItemClass ic = st.itemValue.ItemClass;
            string nm = ic != null && ic.Name != null ? ic.Name : ("type" + st.itemValue.type);
            return nm + " x" + st.count;
        }
    }
}
