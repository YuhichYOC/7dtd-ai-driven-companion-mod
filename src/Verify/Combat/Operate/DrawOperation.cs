/*
*
* DrawOperation.cs
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
    internal class DrawOperation
    {
        private InfoHolder _i;
        private LogInfoHolder _li;

        private Tuple<ItemActionCatapult, ItemActionCatapult.ItemActionDataCatapult> _bow;

        internal bool BowHolding { get => _bow.Item1 != null && _bow.Item2 != null; }

        internal bool ActionActivated { get => _bow.Item2.m_bActivated; }

        private bool _drawing;
        internal bool Drawing { set => _drawing = value; get => _drawing; }

        private bool _pressed;
        internal bool Pressed { set => _pressed = value; get => _pressed; }

        private float _bowNextTry;
        internal float BowNextTry { set => _bowNextTry = value; get => _bowNextTry; }

        internal DrawOperation(InfoHolder i, LogInfoHolder li)
        {
            _i = i;
            _li = li;
        }

        internal void Init() => _bow = _i.GetHeldCatapult();

        internal void CancelDrawing(bool cancelAction)
        {
            if (cancelAction)
            {
                _bow.Item1.CancelAction(_bow.Item2);
            }
            _i.BowDrawing = false;
        }
    }
}
