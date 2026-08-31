/*
 *
 * FullAutoValidator.cs
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

using CompanionAIVerify.Action.Scene;

namespace CompanionAIVerify.Action.Validation;

internal class FullAutoValidator
{
    private readonly InfoHolder _i;

    internal FullAutoValidator(InfoHolder i)
    {
        _i = i;
    }

    // フルオート判定
    // GetBurstCount == 0
    //   BurstRoundCount ... 既定 1 = セミ, 0 = フル, N = バースト
    internal bool IsFullAuto()
    {
        var inv = _i.Self.inventory;
        var hi = inv != null ? inv.holdingItem : null;
        var hid = inv != null ? inv.holdingItemData : null;
        var ra = hi.Actions[0] as ItemActionRanged;
        if (hi == null || hi.Actions == null || hi.Actions.Length == 0) return false;
        if (ra == null || hid == null || hid.actionData == null || hid.actionData.Count == 0) return false;
        return ra.GetBurstCount(hid.actionData[0]) == 0;
    }
}