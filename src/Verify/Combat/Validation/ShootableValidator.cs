/*
 *
 * ShootableValidator.cs
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

using System.Collections.Generic;
using System.Linq;
using CompanionAIVerify.Combat.Scene;
using CompanionAIVerify.Config;
using UnityEngine;

namespace CompanionAIVerify.Combat.Validation;

internal class ShootableValidator
{
    private readonly InfoHolder _i;

    internal ShootableValidator(InfoHolder i)
    {
        _i = i;
    }

    // ★ v0.8(D)
    // 友軍射線ガード
    // 実射 fireShot は GetLookRay().origin ( = 目, EntityAlive : 5536 ) から狙点方向へ拡散付きで飛ぶ
    //   ここでは同一原点 -> aimPoint の直線に対し、対象より手前 ( dist < 狙点距離 ) で友軍の AABB ( 膨張 ) に交差するものが 1 体でもあればホールドする
    //   友軍 = 自分以外の生存プレイヤー + ally ドローン
    //   膨張量 FriendlyFireMargin は拡散 + コライダー幅ぶんの余裕 ( 片側マージン )
    internal List<EntityAlive> FfFriendlies { get; private set; }

    // ★ v0.8(D)
    // 友軍射線ガード
    // 実射 ( fireShot ) と同一原点 ( GetLookRay ) + 同一狙点方向で、対象より手前の射線帯に友軍 ( 他プレイヤー + ally ドローン ) が居れば、狙点が通っていても発砲しない
    //   既存の shootable ( 狙点探索 / 遮蔽 ) は「頭が 1 点通れば撃つ」で緩く、拡散 + 原点差で友軍に当たっていた ( FF 漏れ実測 )
    //   ここで実射ラインを直接検証して塞ぐ
    internal bool FriendlyInLineOfFire()
    {
        var world = _i.Self.world;
        if (world == null) return false;

        var origin = _i.Self.GetLookRay().origin; // 実射と同一原点
        var dir = _i.AimOperator.AimPoint - origin;
        var dlen = dir.magnitude; // 対象狙点までの距離 ( この手前だけ問題 )
        if (dlen < 1e-4f) return false;
        var shotRay = new Ray(origin, dir / dlen);
        var margin = Cfg.FriendlyFireMargin;

        // --- 友軍集合を集める ---
        FfFriendlies = [];
        var players = world.GetPlayers(); // リモートのリーダーも含む ( FindNearestLeader と同経路 )
        if (players != null)
            FfFriendlies =
            [
                .. players
                    .Where(p => p != null)
                    .Where(p => p != _i.Self)
                    .Where(p => !p.IsDead())
                    .Select(p => p as EntityAlive)
            ];

        // ally ドローンのみ友軍に含める ( fireShot : 1449 と同じ isAlly 判定 )
        var ents = world.Entities?.list;
        if (ents != null)
            FfFriendlies.AddRange([
                .. ents
                    .Where(e => e != null)
                    .Where(e => e != _i.Self)
                    .Where(e => !e.IsDead())
                    .Select(e => e as EntityDrone)
                    .Where(e => e != null)
                    .Where(e => e.isAlly(_i.Self))
                    .Select(e => e as EntityAlive)
            ]);

        // --- 射線帯の交差判定 ---
        FfFriendlies =
        [
            .. FfFriendlies
                .Where(f =>
                {
                    var b = f.boundingBox; // world AABB ( Entity.boundingBox )
                    b.Expand(margin * 2.0f); // Expand は総量増加 = 片側 margin
                    return b.IntersectRay(shotRay, out var dist) && dist > 0.0f && dist < dlen;
                })
        ];

        return FfFriendlies.Count > 0;
    }
}