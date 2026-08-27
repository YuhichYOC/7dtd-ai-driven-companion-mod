/*
*
* SwingOperation.cs
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
    internal class SwingOperation
    {
        private InfoHolder _i;
        private LogInfoHolder _li;

        private bool _pressed;
        internal bool Pressed { set => _pressed = value; get => _pressed; }

        internal SwingOperation(InfoHolder i, LogInfoHolder li)
        {
            _i = i;
            _li = li;
        }

        internal void Run()
        {
            if (_i.Self.Attack(false)) // press。ケイデンスは canStartAttack の APM 律速が制御
            {
                _i.FirePressed = true;
                _li.Log.LogMeleeSwing(_i);
            }
        }
    }
}
