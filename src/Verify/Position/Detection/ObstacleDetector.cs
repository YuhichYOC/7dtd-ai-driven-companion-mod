/*
 *
 * ObstacleDetector.cs
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

using CompanionAIVerify.Config;
using CompanionAIVerify.Position.Scene;
using UnityEngine;
using Logger = CompanionAIVerify.Log.Logger;

namespace CompanionAIVerify.Position.Detection;

internal class ObstacleDetector
{
    private readonly InfoHolder _i;

    internal ObstacleDetector(InfoHolder i)
    {
        _i = i;
    }

    // ★ [jump] 前方に「1ブロック段差（乗り越え可能）」があるかを判定し、先手でジャンプする。
    //   接地ゲートのみ: onGround は EPL:2589 で m_vp_FPController.Grounded から更新されるので EPL でも有効。
    //   ★ isCollidedHorizontally は使わない: それを更新する Entity:1834 は直後 1837 の m_characterController.IsGrounded() と
    //     同じ CharacterController 経路にあり、vp_FPController で動く EntityPlayerLocal では通らず常に false のため
    //     （実ログで確認: 段差手前でも not collidedHorizontally が連続）。よって「詰まってから」ではなく
    //     前方ボクセル探査で「詰まる前に」段差を検出して跳ぶ。
    //   乗り越え可否: 進行方向の前方セルで「脛の高さ(+0.5m)にブロック」かつ「頭の高さ(+1.5m)は空」＝段差1ブロック。
    //     2ブロック以上の壁は頭の高さが塞がるので false（無駄ジャンプを抑止）。高さオフセットは position.y の
    //     微小揺れ（地面上面の丸め）に強い +0.5/+1.5 を採用。階段状(1→2→3)は各段が1ブロック差なので順に登れる。
    //   ※ 検証フェーズ: 座標変換(Origin要否/高さ/プローブ)が正しいか実機で追えるよう毎回ログする。安定後に削る。
    internal bool ShouldJumpObstacle(Vector3 moveDir)
    {
        if (!Cfg.JumpObstacles) return false;
        if (!_i.Self.onGround) return false;

        var world = _i.Self.world;
        if (world == null) return false;

        var flat = moveDir;
        flat.y = 0.0f;
        if (flat.sqrMagnitude < 1e-4f) return false; // 前進意図なし
        flat.Normalize();

        // Entity.position はワールド座標（World 内の worldToBlockPos(_position) 呼び出し群と同じ扱い）。
        //   ※ ActionDriver で Origin を足したのは playerCamera.transform.position が Unity レンダ座標だったため。
        //     Entity.position には Origin 補正は不要。Origin.position はログにだけ残し、非ゼロ環境で気付けるようにする。
        var wp = _i.Self.position;
        var ahead = wp + flat * Cfg.JumpProbeAhead;

        var legCell = World.worldToBlockPos(new Vector3(ahead.x, wp.y + 0.5f, ahead.z)); // 脛の高さ
        var headCell = World.worldToBlockPos(new Vector3(ahead.x, wp.y + 1.5f, ahead.z)); // 頭の高さ

        var legBlocked = IsBlocking(world, legCell.x, legCell.y, legCell.z);
        var headClear = !IsBlocking(world, headCell.x, headCell.y, headCell.z);
        var jump = legBlocked && headClear;

        Logger.LogJump(wp, flat, legCell, legBlocked, headCell, headClear, jump);

        return jump;
    }

    // セルが移動を阻害するか。air は通行可、IsCollideMovement=true の実体ブロックのみ阻害。
    //   BlockValue.isair / Block.IsCollideMovement は vanilla の衝突判定と同じ経路（World:2072）。
    private static bool IsBlocking(World world, int x, int y, int z)
    {
        var bv = world.GetBlock(x, y, z);
        if (bv.isair) return false;
        var b = bv.Block;
        return b != null && b.IsCollideMovement;
    }
}