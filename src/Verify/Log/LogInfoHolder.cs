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

namespace CompanionAIVerify.Log;

internal static class LogInfoHolder
{
    // CombatDriver など、各クラスの持つ状態のうちログに関するものを LogInfo にまとめる
    //
    // Log クラスも同居させる

    // 交戦ログ throttle
    internal static float NextEngageLogTime { get; set; }

    // ホールド理由ログの throttle
    internal static float NextHoldLogTime { get; set; }

    // 弓ログ throttle
    internal static float NextBowLogTime { get; set; }

    // ジャンプログ throttle
    internal static float NextJumpLogTime { get; set; }

    // 脅威ログ throttle
    internal static float NextThreatLogTime { get; set; }

    // 発砲ドライバ「前回ログ時点でのマガジン残弾数」
    internal static int LastMeta { get; set; }

    internal int LastLoggedThreatId { get; set; }

    internal Logger Logger { get; }
}