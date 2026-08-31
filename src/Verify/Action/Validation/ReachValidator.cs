/*
 *
 * ReachValidator.cs
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
using CompanionAIVerify.Config;
using CompanionAIVerify.Position;
using UnityEngine;

namespace CompanionAIVerify.Action.Validation;

internal class ReachValidator
{
    private readonly InfoHolder _i;

    internal ReachValidator(InfoHolder i)
    {
        _i = i;
    }

    // ★ v0.8(C)
    // 射程ゲート
    //   グローバル上限 ( RangedMaxEngageMeters ) に加え、武器固有の実効射程でも「弾が届かない距離」を弾く
    //   fireMax = min ( グローバル上限, 実効射程 × 安全係数 )
    //   実効射程 = EngageRange.Read().range ( ranged では GetRange() = MaxRange 適用後の発射射程, ItemActionRanged:1376 )
    //   Slice A 実測で shotgun range ≈ 10 なのに d ≈ 20 で撃って弾が届かない問題を解消する
    //   d は feet-to-feet、実際の弾は camera -> aimPoint なので安全係数 ( 既定 0.85 ) で余裕を持たせる
    //   ※ [bow] 弓も ItemActionRanged 派生なので GetRange が取れる。ただし矢は放物線弾道で直線射程とズレる
    //     fireShot は無効化 : Launcher:120-125。落下 / リードの弾道補正は本スライスのスコープ外とする
    internal bool TargetInReach()
    {
        var reach = _i.Reach;
        var d = _i.Distance;
        if (_i.Ranged)
        {
            var fireMax = Cfg.RangedMaxEngageMeters;
            var erC = EngageRange.Read(_i.Self);
            if (erC.valid && erC.isRanged && erC.range > 0.01f)
                fireMax = Mathf.Min(fireMax, erC.range * Cfg.RangedRangeSafety);

            return _i.Distance <= fireMax;
        }

        return _i.Distance <= _i.Reach + Cfg.ReachBuffer;
    }
}