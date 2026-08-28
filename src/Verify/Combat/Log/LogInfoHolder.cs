/*
 *
 * LogInfoHolder.cs
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

namespace CompanionAIVerify.Combat.Log;

internal class LogInfoHolder
{
    // CombatDriver の持つ状態のうちログに関するものを LogInfo にまとめる
    //
    // Log クラスも同居させる

    internal LogInfoHolder()
    {
        LastMeta = int.MinValue;
        Logger = new Logger(this);
    }

    internal float NextEngageLogTime { get; set; }

    // ホールド理由ログの throttle
    internal float NextHoldLogTime { get; set; }

    // 弓ログ throttle
    internal float NextBowLogTime { get; set; }

    internal int LastMeta { get; set; }

    internal Logger Logger { get; }
}