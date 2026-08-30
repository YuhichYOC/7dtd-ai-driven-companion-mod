/*
 *
 * AdsOperation.cs
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
using CompanionAIVerify.Config;

namespace CompanionAIVerify.Combat.Operate;

internal class AdsOperation
{
    private readonly InfoHolder _i;

    internal AdsOperation(InfoHolder i)
    {
        _i = i;
    }

    // ADS ( サイト覗き ) 状態
    // 変化時のみトグル
    internal bool AdsOn { get; private set; }

    // ADS ( サイト覗き ) 状態を変化時のみ切替
    // AimingGun setter は FOV / animator / Actions[1] に副作用があるため冪等呼び出しを避ける
    // secondary action を持たない銃では発動しない
    internal void Run(bool on)
    {
        if (on && !Cfg.AimDownSightsOnEngage) on = false;
        if (on && !_i.AdsActionValidator.CanUseAds()) on = false;
        if (on == AdsOn) return;
        AdsOn = on;
        _i.Self.AimingGun = on; // 拡散 hip ( 1.0 ) <-> aiming ( 0.1 ) を切替 ( ItemActionRanged : 748, 1346 )
    }

    // ItemActionLauncher ( クロスボウ・ロケットランチャー ), LauncherPatternXX 専用
    // 内容が Run とほぼ同じ, Run への副作用を防止するため分けて実装している
    // Run へ渡すフラグにより分岐を増やすか考え中
    internal void RunLauncher(bool on)
    {
        if (on && !Cfg.AimDownSightsOnEngage) on = false;
        if (on && !_i.AdsActionValidator.CanUseLauncherAds()) on = false;
        if (on == AdsOn) return;
        AdsOn = on;
        _i.Self.AimingGun = on; // 拡散 hip ( 1.0 ) <-> aiming ( 0.1 ) を切替 ( ItemActionRanged : 748, 1346 )
    }
}