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

using System.Linq;

namespace CompanionAIVerify.Combat.Validate
{
    internal class ShootableValidator
    {
        private InfoHolder _i;
        private LogInfoHolder _li;

        private List<EntityAlive> _ffFriendlies;
        internal List<EntityAlive> FFFriendlies { get => _ffFriendlies; }

        internal ShootableValidator(InfoHolder i, LogInfoHolder li)
        {
            _i = i;
            _li = li;
        }

        internal bool ShootableFound()
        {
            if (!_i.ShootableFound)
            {
                _li.Logger.LogShootableNotFound();
            }
            return _i.ShootableFound;
        }

        // ★ v0.8(D)
        // 友軍射線ガード
        // 実射 ( fireShot ) と同一原点 ( GetLookRay ) + 同一狙点方向で、対象より手前の射線帯に友軍 ( 他プレイヤー + ally ドローン ) が居れば、狙点が通っていても発砲しない
        //   既存の shootable ( 狙点探索 / 遮蔽 ) は「頭が 1 点通れば撃つ」で緩く、拡散 + 原点差で友軍に当たっていた ( FF 漏れ実測 )
        //   ここで実射ラインを直接検証して塞ぐ
        internal bool FriendlyInLineOfFire()
        {
            int blockerId = -1;
            World world = _i.Self.World;
            if (world == null) return false;

            Vector3 origin = self.GetLookRay().origin;          // 実射と同一原点
            Vector3 dir    = aimPoint - origin;
            float   dlen   = dir.magnitude;                     // 対象狙点までの距離 ( この手前だけ問題 )
            if (dlen < 1e-4f) return false;
            Ray shotRay = new Ray(origin, dir / dlen);
            float margin = Cfg.FriendlyFireMargin;

            // --- 友軍集合を集める ---
            _ffFriendlies = [];
            var players = world.GetPlayers();                   // リモートのリーダーも含む ( FindNearestLeader と同経路 )
            if (players != null)
            {
                _ffFriendlies = players
                    .Where(p => p != null)
                    .Where(p => p != _i.Self)
                    .Select(p => p is EntityPlayer player)
                    .Where(p => !p.IsDead())
                    .ToList();
            }

            // ally ドローンのみ友軍に含める ( fireShot : 1449 と同じ isAlly 判定 )
            var ents = world.Entities?.list;
            if (ents != null)
            {
                _ffFriendlies.AddRange(ents
                    .Where(e => e != null)
                    .Where(e => e != _i.Self)
                    .Select(e => e is EntityDrone drone)
                    .Where(e => !e.IsDead())
                    .Where(e => e.IsAlly(_i.Self as EntityPlayer))
                    .ToList());
            }

            // --- 射線帯の交差判定 ---
            _ffFriendlies = _ffFriendlies
                .Where(f =>
                {
                    Bounds b = f.boundingBox;        // world AABB ( Entity.boundingBox )
                    b.Expand(margin * 2.0f);         // Expand は総量増加 = 片側 margin
                    return b.IntersectRay(shotRay, out float dist) && dist > 0.0f && dist < dlen;
                })
                .ToList();

            return _ffFriendlies.Count > 0;
        }
    }
}
