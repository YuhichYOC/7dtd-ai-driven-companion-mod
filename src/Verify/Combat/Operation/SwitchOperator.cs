/*
 *
 * SwitchOperator.cs
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

namespace CompanionAIVerify.Combat.Operation;

internal class SwitchOperator
{
    private readonly InfoHolder _i;

    internal SwitchOperator(InfoHolder i)
    {
        _i = i;
    }

    internal bool Switched { get; private set; }

    internal void Run()
    {
        // v0.8.3
        // 武器切り替えは WeaponSelector.ApplyMode に一元化
        // settle も executor 側へ寄せた
        // 現バージョンではこのクラスを残す
        // 削除予定
        Switched = false;
    }
}