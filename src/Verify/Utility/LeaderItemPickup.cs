/*
 *
 * LeaderItemPickup.cs
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

extern alias LogLib;
using System.Collections.Generic;
using CompanionAIVerify.Config;
using UnityEngine;

namespace CompanionAIVerify.Utility;

// --- (C) リーダー落下物拾得 ----------------------------------------------
//   navigator=null で自律移動不可のため「検知半径内で Collect を撃つ」方針（移動しない）。
//   Collect は非サーバ側では NetPackageEntityCollect をサーバへ送るのみ（Entity:3202）。
//   実デポジット挙動（EntityItem.OnCollectServer）と手動ドロップ品の belongsPlayerId
//   セマンティクスは実機ログで確定する（本ハーネスの検証対象）。
internal static class LeaderItemPickup
{
    private static readonly List<Entity> _buf = new();
    private static float _nextScan;
    private static float _nextLog;

    internal static void MaybeRun(EntityPlayerLocal self, EntityPlayer leader)
    {
        if (!Cfg.AutoPickupLeaderDrops || leader == null) return;
        if (Time.time < _nextScan) return;
        _nextScan = Time.time + Cfg.PickupScanIntervalSec;

        var world = self.world;
        if (world == null) return;

        var r = Cfg.PickupRadius;
        var box = new Bounds(self.position, new Vector3(r * 2f, r * 2f, r * 2f));
        _buf.Clear();
        // クラスフィルタ列挙。EntityItem は非生存だがこの版は class で拾う（World:2390）。
        world.GetEntitiesInBounds(typeof(EntityItem), box, _buf);
        if (_buf.Count == 0) return;

        var rSq = r * r;
        int seen = 0, collected = 0;
        var firstOwner = int.MinValue; // 診断: 最初の item の所有者
        for (var i = 0; i < _buf.Count; i++)
        {
            var e = _buf[i];
            if (e == null || e.IsDead()) continue;
            var dSq = (e.position - self.position).sqrMagnitude;
            if (dSq > rSq) continue;

            seen++;
            if (firstOwner == int.MinValue) firstOwner = e.belongsPlayerId;

            var leaderOwned = e.belongsPlayerId == leader.entityId;
            var freeToGrab = Cfg.PickupUnowned && e.belongsPlayerId <= 0;
            if (leaderOwned || freeToGrab)
            {
                e.Collect(self.entityId); // サーバへ NetPackageEntityCollect
                collected++;
                LogLib::Log.Out(
                    $"[CompanionAI] pickup: collect id={e.entityId} owner={e.belongsPlayerId} d={Mathf.Sqrt(dSq):0.0}m ({(leaderOwned ? "leader" : "unowned")})");
            }
        }

        // 近傍 item の所有者セマンティクス確認用の throttled 診断。
        if (seen > 0 && collected == 0 && Time.time >= _nextLog)
        {
            _nextLog = Time.time + 1.0f;
            LogLib::Log.Out(
                $"[CompanionAI] pickup: {seen} item(s) in range, none matched (firstOwner={firstOwner}, leaderId={leader.entityId}, selfId={self.entityId}).");
        }
    }
}