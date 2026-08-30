/*
 *
 * WeaponClassifier.cs
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
using System.Collections.Generic;
using CompanionAIVerify.Config;
using Logger = CompanionAIVerify.Log.Logger;

namespace CompanionAIVerify.ToolSelection;

// --- 武器分類器（stow / select 共用の単一チョークポイント） ---------------
//   優先: 除外(名前/タグ) > 名前 include > タグ include > auto時のみ型フォールバック。
//   parse 済みセットはキャッシュし、Rebuild() でのみ再構築（毎フレーム alloc 回避）。
internal static class WeaponClassifier
{
    private static bool _built;
    private static HashSet<string> _meleeNames = Empty();
    private static HashSet<string> _rangedNames = Empty();
    private static HashSet<string> _excludeNames = Empty();
    private static FastTags<TagGroup.Global> _meleeTags, _rangedTags, _excludeTags;
    private static bool _hasMeleeTags, _hasRangedTags, _hasExcludeTags;

    private static HashSet<string> Empty()
    {
        return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    // config 読込後（F8 等）に呼ぶ。RefreshLoadout から駆動される。
    internal static void Rebuild()
    {
        _built = true;
        _meleeNames = ParseNames(Cfg.MeleeWeaponNames);
        _rangedNames = ParseNames(Cfg.RangedWeaponNames);
        _excludeNames = ParseNames(Cfg.WeaponExcludeNames);
        _meleeTags = ParseTags(Cfg.MeleeWeaponTags, out _hasMeleeTags);
        _rangedTags = ParseTags(Cfg.RangedWeaponTags, out _hasRangedTags);
        _excludeTags = ParseTags(Cfg.WeaponExcludeTags, out _hasExcludeTags);

        var strict = Cfg.WeaponClassifyMode == "strict";
        var noIncludes = _meleeNames.Count == 0 && _rangedNames.Count == 0
                                                && !_hasMeleeTags && !_hasRangedTags;
        if (strict && noIncludes)
            Logger.LogWeaponClassifyStrictNoIncludes();
        else
            Logger.LogWeaponClassify(_meleeNames.Count, _rangedNames.Count, _excludeNames.Count, _hasMeleeTags ? 1 : 0,
                _hasRangedTags ? 1 : 0, _hasExcludeTags ? 1 : 0);
    }

    internal static WeaponMode Classify(ItemClass ic)
    {
        if (ic == null) return WeaponMode.None;
        if (!_built) Rebuild();

        var name = ic.Name ?? "";

        // 1) 除外（最優先）
        if (_excludeNames.Contains(name)) return WeaponMode.None;
        if (_hasExcludeTags && ic.ItemTags.Test_AnySet(_excludeTags)) return WeaponMode.None;

        // 2) 名前 include
        if (_rangedNames.Contains(name)) return WeaponMode.Ranged;
        if (_meleeNames.Contains(name)) return WeaponMode.Melee;

        // 3) タグ include
        if (_hasRangedTags && ic.ItemTags.Test_AnySet(_rangedTags)) return WeaponMode.Ranged;
        if (_hasMeleeTags && ic.ItemTags.Test_AnySet(_meleeTags)) return WeaponMode.Melee;

        // 4) auto のみ：型フォールバック（工具＝dynamic melee も近接に含む）
        if (Cfg.WeaponClassifyMode != "strict")
        {
            var a = ic.Actions != null && ic.Actions.Length > 0 ? ic.Actions[0] : null;
            if (a is ItemActionRanged) return WeaponMode.Ranged;
            if (a is ItemActionMelee || a is ItemActionDynamicMelee) return WeaponMode.Melee;
        }

        return WeaponMode.None;
    }

    private static HashSet<string> ParseNames(string csv)
    {
        var set = Empty();
        if (!string.IsNullOrEmpty(csv))
            foreach (var s in csv.Split(','))
            {
                var t = s.Trim();
                if (t.Length > 0) set.Add(t);
            }

        return set;
    }

    private static FastTags<TagGroup.Global> ParseTags(string csv, out bool has)
    {
        var c = csv != null ? csv.Trim() : "";
        has = c.Length > 0;
        return has ? FastTags<TagGroup.Global>.Parse(c) : default;
    }
}