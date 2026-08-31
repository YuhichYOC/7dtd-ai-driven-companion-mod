/*
 *
 * FaceOperator.cs
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

using CompanionAIVerify.Position.Scene;
using UnityEngine;

namespace CompanionAIVerify.Position.Operation;

internal class FaceOperator
{
    private readonly InfoHolder _i;

    internal FaceOperator(InfoHolder i)
    {
        _i = i;
    }

    internal void Run(Vector3 worldDir)
    {
        if (worldDir.sqrMagnitude < 1e-6f) return;
        var euler = Quaternion.LookRotation(worldDir.normalized, Vector3.up).eulerAngles;
        euler.x *= -1.0f;
        _i.Self.SetRotation(euler);
    }
}