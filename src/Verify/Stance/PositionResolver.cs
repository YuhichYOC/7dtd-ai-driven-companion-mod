/*
 *
 * PositionResolver.cs
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

using CompanionAIVerify.Perception;

namespace CompanionAIVerify.Stance;

/*
 * MOD 操作キャラクターの位置調整を解決する
 */
internal class PositionResolver
{
    internal PositionResolver()
    {
        Action = Actions.None;
    }

    internal Actions Action { get; private set; }

    internal void Run(EntityPlayerLocal self, in ThreatInfo threat)
    {
        // 仮実装 ... 常に ver 0.8.1 のリーダー追従を行う
        Action = Actions.Follow01;
    }

    internal enum Actions
    {
        None,
        Follow01
    }
}