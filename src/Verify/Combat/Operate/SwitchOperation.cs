/*
*
* SwitchOperation.cs
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
    internal class SwitchOperation
    {
        private InfoHolder _i;
        private LogInfoHolder _li;

        internal SwitchOperation(InfoHolder i, LogInfoHolder li)
        {
            _i = i;
            _li = li;
        }

        internal void Run()
        {
            // ★ v0.7(A)
            // 交戦距離に応じた武器自動切替
            // 切替した frame は settle のため即 return
            WeaponSelector.RefreshLoadout(_i.Self, force: false);
            if (Cfg.AutoWeaponSwitch && WeaponSelector.MaybeSwitch(_i.Self, _i.Distance))
            {
                // ★ [bow] 切替で武器が変わる前に、押下中のトリガー / ドローを安全開放する
                //   弓ドロー中は release = 発射になるため、ここを通さないと切替の瞬間に暴発しうる
                _i.ReleaseOperation.Run();
            }
        }
    }
}
