/*
 *
 * FaceOperation.cs
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
using UnityEngine;

namespace CompanionAIVerify.Combat.Operate;

internal class FaceOperation
{
    private readonly InfoHolder _i;

    internal FaceOperation(InfoHolder i)
    {
        _i = i;
    }

    // ピッチ込みで対象中心付近を狙う
    // 低い脅威にも当てるため y を潰さない
    // 変換式は facing と同一 ( EPL : 2310, 248-252
    // camera 経由で攻撃レイが操舵される
    internal void Run()
    {
        var eye = _i.Self.position + Vector3.up * 1.5f; // 概算カメラ高
        var aim = _i.Target.Target.position + Vector3.up * 0.9f; // 概算胴中心
        var dir = aim - eye;
        if (dir.sqrMagnitude < 1e-6f) return;

        var euler = Quaternion.LookRotation(dir.normalized, Vector3.up).eulerAngles;
        euler.x *= -1f; // ピッチ反転（EPL:239, 251）
        _i.Self.SetRotation(euler);
    }
}