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

        internal DrawOperation(InfoHolder i, LogInfoHolder li)
        {
            _i = i;
            _li = li;
        }

        internal void Init() => _bow = _i.GetHeldCatapult();

        internal void CancelDrawing()
        {
            if (!Cfg.BowChargeEnabled)
            {
                // ドロー無効時は弓を撃たない
                // ドローなしでは strain ≈ 0 で実用にならない
                // ドロー中なら安全に引き戻す
                if (_i.BowDrawing)
                {
                    _bow.Item1.CancelAction(_bow.Item2);
                    _i.BowDrawing = false;
                }
            }

            // ゲーム側キャンセル
            //   Catapult : 141 - 145 等
            //     - 矢切れ
            //     - 武器切替
            //     - TP カメラ NG
            // で活性が落ちたら状態同期
            if (_i.BowDrawing && _bow.Item2.m_bActivated)
            {
                _i.BowDrawing = false;
            }
        }
    }
}