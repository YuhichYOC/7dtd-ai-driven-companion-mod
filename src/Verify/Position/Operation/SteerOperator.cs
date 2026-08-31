/*
 *
 * SteerOperator.cs
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

internal class SteerOperator
{
    private readonly InfoHolder _i;

    internal SteerOperator(InfoHolder i)
    {
        _i = i;
    }

    internal void Run(Vector3 moveTarget, Vector3 lookDir, bool running)
    {
        // 目的地 moveTarget に近いか判定
        // 移植元メソッド Steer では副作用 toTarget を受け取っていた
        // PositionValidator は toTarget をメンバー変数として封じ込めている -> _lookFwd 計算にも _toTarget を利用している
        if (_i.PositionValidator.Arrived(moveTarget))
        {
            _i.HaltOperator.Run();
            return;
        }

        var moveWorld = _i.PositionValidator.ToTarget.normalized;
        if (_i.PositionValidator.ShouldRotate(lookDir)) _i.FaceOperator.Run(lookDir);

        var lookRight = Vector3.Cross(Vector3.up, _i.PositionValidator.LookFwd);
        _i.Self.movementInput.moveForward =
            Mathf.Clamp(Vector3.Dot(moveWorld, _i.PositionValidator.LookFwd), -1.0f, 1.0f);
        _i.Self.movementInput.moveStrafe = Mathf.Clamp(Vector3.Dot(moveWorld, lookRight), -1.0f, 1.0f);
        _i.Self.movementInput.running = running;

        // ★ [jump] 進行方向に「乗り越え可能な1ブロック段差」があればジャンプで越える
        //   jump は EPL : 3526 で inputWasJump とのエッジ比較 + onGround ゲート ( EPL : 3530 )
        //   詰まっている間 true を返し続けても、初回発火 -> 空中 ( onGround = false で false ) -> 着地で再評価、と自然に 1 回ずつジャンプする
        _i.Self.movementInput.jump = _i.ObstacleDetector.ShouldJumpObstacle(moveWorld);
        _i.Self.movementInput.down = false;
    }
}