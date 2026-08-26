/*
*
* ADSOperation.cs
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

namespace CompanionAIVerify.Combat.Operate
{
    internal class ADSOperation
    {
        private InfoHolder _i;
        private LogInfoHolder _li;

        internal ADSOperation(InfoHolder i, LogInfoHolder li)
        {
            _i = i;
            _li = li;
        }

        // ADS ( サイト覗き ) 状態を変化時のみ切替
        // AimingGun setter は FOV / animator / Actions[1] に副作用があるため冪等呼び出しを避ける
        // secondary action を持たない銃では発動しない
        internal void Run(bool on)
        {
            if (on && !Cfg.AimDownSightsOnEngage) on = false;
            _i.ADSActionValidator.Run();
            if (on && !_i.CanAds) on = false;
            if (on == _i.AdsOn) return;
            _i.AdsOn = on;
            _i.Self.AimingGun = on; // 拡散 hip ( 1.0 ) <-> aiming ( 0.1 ) を切替 ( ItemActionRanged : 748, 1346 )
        }
    }
}
