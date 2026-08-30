/*
 *
 * ADSActionValidator.cs
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

using CompanionAIVerify.Combat.Scene;

namespace CompanionAIVerify.Combat.Validate;

internal class AdsActionValidator
{
    private readonly InfoHolder _i;

    internal AdsActionValidator(InfoHolder i)
    {
        _i = i;
    }

    // ADS 可否
    // secondary action ( Actions[1] ) と actionData[1] が存在すること
    // AimingGun setter が actionData[1] を直接参照 = 境界外で例外になるためのガード
    internal bool CanUseAds()
    {
        var inv = _i.Self.inventory;
        var hi = inv != null ? inv.holdingItem : null;
        var hid = inv != null ? inv.holdingItemData : null;
        // 構造ガード（既存・残す）: setter(EntityAlive:1495) が actionData[1] を触るため必須
        if (hi == null || hi.Actions == null || hi.Actions.Length < 2 || hi.Actions[1] == null) return false;
        if (hid == null || hid.actionData == null || hid.actionData.Count < 2) return false;
        // 意味ガード（追加）: リロード中は ADS しない（ItemActionRanged:910 = NotReloading:786）
        if (!(hi.Actions[0] is ItemActionRanged ranged)) return false;
        return ranged.IsAimingGunPossible(hid.actionData[0]);
    }

    // LauncherPatternXX 専用
    // secondary action ( Actions[1] ) を要求しない点だけが CanUseAds() との違い
    // ゲーム公式 EntityPlayerLocal.IsAimingGunPossible ( : 4456 ) と同じく Actions[0] の可否のみで判定する
    //   ItemActionRanged.IsAimingGunPossible ( : 910 ) = NotReloading ( : 786 )
    //     -> isReloading / isWeaponReloading / isReloadRequested がすべて非アクティブなら true
    //   Launcher / Catapult は Ranged の override を継承するため、クロスボウでもこの経路で true になる
    internal bool CanUseLauncherAds()
    {
        var inv = _i.Self.inventory;
        var hi = inv != null ? inv.holdingItem : null;
        var hid = inv != null ? inv.holdingItemData : null;
        if (hi == null || hi.Actions == null || hi.Actions.Length == 0) return false;
        if (hid == null || hid.actionData == null || hid.actionData.Count == 0) return false;

        // NotReloading が ItemActionDataRanged へキャストするため、Ranged 派生を確認してから委譲。
        if (!(hi.Actions[0] is ItemActionRanged ranged)) return false;
        return ranged.IsAimingGunPossible(hid.actionData[0]);
    }
}