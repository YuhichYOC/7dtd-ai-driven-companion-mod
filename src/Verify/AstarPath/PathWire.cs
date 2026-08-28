/*
 *
 * PathWire.cs
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
 *
 */

// =============================================================================
// navigation スライス2 : 送信路（チャットPoC）— 共有コーデック＋ホスト送信＋クライアント再結合
//
// ねらい:
//   スライス1で抽出した経路 Vector3[]（＋状態 REACHED/APPROACHING/STUCK）を、ホストから
//   コンパニオンのクライアントへチャット経由で送り、クライアントで無損失に再結合できることを
//   ログ突き合わせで検証する。まだ動かさない（追従はスライス3）。
//
// 確定した送受信経路（3.1.0 逆コンパイル実物。file:line）:
//   - 送信は GameManager.ChatMessageServer(cInfo, chatType, senderEntityId, msg,
//       recipientEntityIds, msgSender, bbMode)（GameManager:4470）。
//       ・recipientEntityIds=[companion] のとき:
//           4481 ChatMessageClient(...) をホスト自身に呼ぶが、4520 の受信者チェックで
//                リーダー(=ホストの local player)は対象外 → ホスト画面には出ない。
//           4496-4501 recipient の client にだけ NetPackageChat を送る（他プレイヤーに届かない）。
//       ・IsServer ガード（4472）内でのみ送出。→ ホスト限定で呼ぶ。
//   - 受信は client 側 NetPackageChat.ProcessPackage → ChatMessageClient(...)（NetPackageChat:82）
//       → XUiC_ChatOutput.AddMessage（GameManager:4522）で表示。
//       ここを ChatMessageClient の prefix で横取りし、AddMessage 前に抑止する（PathRx.cs）。
//       抑止するため、テキストフィルタ/bbcode も通らない → ペイロード文字種は自由。
//
// ワイヤ形式（1メッセージ＝1チャンク。'|' 区切り8フィールド）:
//   ~CAIP~|<msgId>|<seq>|<total>|<status>|<n>|<ax>,<ay>,<az>|<payload>
//     payload = 各点 "dx,dy,dz"（cm整数、anchor からの相対）を ';' 連結したもの。
//     anchor(ax,ay,az) = 送信時のコンパニオン位置（cm整数・絶対）。
//     復元: 絶対点 = (ax+dx, ay+dy, az+dz) / 100 [m]。
//   点境界でのみチャンク分割するので、payload を ';' 連結すれば元の点列に戻る。
//
// 導入:
//   本ファイルは host/companion 両方の同一アセンブリに含める（両PCへ配置）。
//   送信は MaybeSendFromHost（ホスト側 HostPathProbe から呼ばれる）。
//   受信は OnChunkClient（クライアント側 PathRx prefix から呼ばれる）。
// =============================================================================

extern alias LogLib;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using CompanionAIVerify.Positioning;
using UnityEngine;

namespace CompanionAIVerify.AstarPath;

internal static class PathWire
{
    // --- Tunables ---------------------------------------------------------------------------
    internal const string Tag = "~CAIP~"; // 自タグ。人間チャットと衝突しにくく bbcode 文字を含まない

    // enum メンバは添付 enum 実体で未確認。コンパイルで弾かれたらここだけ実 enum に合わせて差し替え。
    //   EChatType.Global / EMessageSender.None は表示側の値で、本経路では抑止するため機能に影響しない。
    internal const EChatType WireChatType = EChatType.Global;
    internal const EMessageSender WireMsgSender = EMessageSender.None;
    internal static bool SendEnabled = true; // 送信の有効 / 無効
    internal static int ChunkChars = 350; // 1 メッセージあたり payload の最大文字数 ( 点境界で分割 )
    internal static float SendThrottleSec = 1.5f; // 送信の最小間隔 ( 状態 / 点数変化時は即時 )
    internal static float RxStaleSec = 5.0f; // 未完了バッファの破棄猶予

    // --- 送信状態 ( ホスト側 ) ----------------------------------------------------------------
    private static int _msgId;
    private static float _nextSendTime;
    private static string _lastSig = string.Empty;

    private static readonly Dictionary<int, RxBuf> _rx = new();

    // ============================ ホスト送信 ============================
    internal static void MaybeSendFromHost(EntityPlayer companion, Vector3[] wps, string status)
    {
        if (!SendEnabled || companion == null || wps == null || wps.Length == 0) return;

        WorldInfo.FillGameManager();
        WorldInfo.FillWorld();
        if (WorldInfo.WorldIsNull) return;
        if (WorldInfo.WorldIsRemote) return;

        WorldInfo.Now = Time.time;
        var sig = $"{status}:{wps.Length}"; // 状態 or 点数が変われば即送信
        if (sig == _lastSig && WorldInfo.Now < _nextSendTime) return;
        _lastSig = sig;
        _nextSendTime = WorldInfo.Now + SendThrottleSec;

        var msgId = ++_msgId;
        var chunks = Encode(wps, companion.position, status, msgId);
        var recipients = new List<int> { companion.entityId };
        chunks.ForEach(chunk => WorldInfo.GameManager.ChatMessageServer(null, WireChatType, -1, chunk,
            recipients, WireMsgSender));

        Vector3 s = wps[0], e = wps[wps.Length - 1];
        LogLib::Log.Out($"[CompanionAI][host] sent path msgId={msgId} status={status} pts={wps.Length} " +
                        $"chunks={chunks.Count} start=({s.x:0.0},{s.y:0.0},{s.z:0.0}) end=({e.x:0.0},{e.y:0.0},{e.z:0.0})");
    }

    private static List<string> Encode(Vector3[] wps, Vector3 anchor, string status, int msgId)
    {
        var ax = Mathf.RoundToInt(anchor.x * 100f);
        var ay = Mathf.RoundToInt(anchor.y * 100f);
        var az = Mathf.RoundToInt(anchor.z * 100f);

        // 各点を anchor 相対 cm 整数の "dx,dy,dz" にする
        var ptStrs = wps
            .Select(wp =>
                $"{Mathf.RoundToInt(wp.x * 100f) - ax},{Mathf.RoundToInt(wp.y * 100f) - ay},{Mathf.RoundToInt(wp.z * 100f) - az}")
            .ToList();

        // 点境界で貪欲チャンク（1点が ChunkChars を超える場合も最低1点は入れる）
        var payloads = new List<string>();
        var sb = new StringBuilder();
        // ChunkChars 文字ずつ & アイテムを途中で区切らないように連結しながら ptStrs の内容を payloads へコピーする
        for (var i = 0; i < ptStrs.Count; i++)
        {
            var add = ptStrs[i].Length + (sb.Length > 0 ? 1 : 0);
            if (sb.Length > 0 && sb.Length + add > ChunkChars)
            {
                payloads.Add(sb.ToString());
                sb.Length = 0;
            }

            if (sb.Length > 0) sb.Append(';');
            sb.Append(ptStrs[i]);
        }

        if (sb.Length > 0) payloads.Add(sb.ToString());
        if (payloads.Count == 0) payloads.Add("");

        var total = payloads.Count;
        var anchorStr = $"{ax},{ay},{az}";
        // Select((p, i) ... ) ならランタイムが i にアイテムのインデックスをセットしてくれる
        return [.. payloads.Select((p, i) => $"{Tag}|{msgId}|{i}|{total}|{status}|{wps.Length}|{anchorStr}|{p}")];
    }

    internal static void OnChunkClient(string msg)
    {
        PruneStale();

        var i = Input.TryParse(msg);
        if (i == null) return;

        RxBuf b;
        if (!_rx.TryGetValue(i.msgId, out b))
        {
            if (i.total <= 0) return;
            b = new RxBuf
            {
                total = i.total, n = i.n, status = i.status, ax = i.ax, ay = i.ay, az = i.az,
                parts = new string[i.total], got = 0, firstSeen = Time.time
            };
            _rx[i.msgId] = b;
        }

        if (i.seq >= 0 && i.seq < b.total && b.parts[i.seq] == null)
        {
            b.parts[i.seq] = i.payload;
            b.got++;
        }

        // まだ全チャンクが到着していない -> 何もしない
        if (b.got < b.total) return;

        // 全チャンク到着 -> 再結合
        _rx.Remove(i.msgId);

        // 送信側が Encode にて点 "dx,dy,dz" を ';' で連結したのと逆の操作で座標を復元する
        // 「payload を ';' 連結すれば元の点列に戻る」が表すのは以下のコード
        var joined = b.parts.Aggregate(new StringBuilder(), (seed, p) => seed.Append(seed.Length > 0 ? $";{p}" : p));
        var pts = joined.ToString().Split(';')
            .Select(p =>
            {
                var c = p.Split(',');
                if (c.Length < 3 || !int.TryParse(c[0], out var l_dx) || !int.TryParse(c[1], out var l_dy) ||
                    !int.TryParse(c[2], out var l_dz))
                {
                    LogLib::Log.Warning($"[CompanionAI][client] malformed point '{p}'");
                    return new { count = 0, dx = 0, dy = 0, dz = 0 };
                }

                return new { count = c.Length, dx = l_dx, dy = l_dy, dz = l_dz };
            })
            .Where(p => p.count == 3)
            .Select(p => new Vector3((b.ax + p.dx) / 100.0f, (b.ay + p.dy) / 100.0f, (b.az + p.dz) / 100.0f))
            .ToList();

        var s = pts.Count > 0 ? pts[0] : Vector3.zero;
        var e = pts.Count > 0 ? pts[pts.Count - 1] : Vector3.zero;
        var verdict = pts.Count == b.n ? "OK" : "COUNT-MISMATCH";
        LogLib::Log.Out($"[CompanionAI][client] recv path msgId={i.msgId} status={b.status} " +
                        $"pts={pts.Count}(exp {b.n}) {verdict} chunks={b.total} " +
                        $"start=({s.x:0.0},{s.y:0.0},{s.z:0.0}) end=({e.x:0.0},{e.y:0.0},{e.z:0.0})");

        // スライス3: 受信経路を追従ステートへ渡す（F8ドライバの follow が読む）。
        //   COUNT-MISMATCH は破損の可能性があるので追従へは渡さない（直線フォールバックのまま）。
        if (verdict == "OK" && pts.Count > 0)
            PathFollowState.SetPath(pts.ToArray(), b.status);
    }

    // 期限切れの RxBuf を _rx から削除する
    private static void PruneStale()
    {
        if (_rx.Count == 0) return;
        var now = Time.time;
        List<int> drop = null;
        foreach (var kv in _rx)
            if (now - kv.Value.firstSeen > RxStaleSec)
            {
                if (drop == null) drop = new List<int>();
                drop.Add(kv.Key);
            }

        if (drop != null)
            for (var i = 0; i < drop.Count; i++)
            {
                LogLib::Log.Warning($"[CompanionAI][client] dropped incomplete msgId={drop[i]} (got<total, stale)");
                _rx.Remove(drop[i]);
            }
    }

    // ============================ クライアント受信 ============================
    private class RxBuf
    {
        internal float firstSeen;
        internal int got;
        internal string[] parts;
        internal string status;
        internal int total, n, ax, ay, az;
    }

    private class Input
    {
        internal int ax, ay, az;
        internal int msgId, seq, total, n;
        internal string payload;
        internal string status;

        internal static Input TryParse(string msg)
        {
            // Tag|msgId|seq|total|status|n|ax,ay,az|payload -> 8 分割 ( payload に '|' は無い )
            var f = msg.Split(['|'], 8);

            if (f.Length < 8)
            {
                LogLib::Log.Warning("[CompanionAI][client] malformed chunk (fields < 8)");
                return null;
            }

            if (!int.TryParse(f[1], out var l_msgId) || !int.TryParse(f[2], out var l_seq) ||
                !int.TryParse(f[3], out var l_total) || !int.TryParse(f[5], out var l_n))
            {
                LogLib::Log.Warning("[CompanionAI][client] malformed chunk (header parse)");
                return null;
            }

            var a = f[6].Split(',');
            if (a.Length < 3 || !int.TryParse(a[0], out var l_ax) || !int.TryParse(a[1], out var l_ay) ||
                !int.TryParse(a[2], out var l_az))
            {
                LogLib::Log.Warning("[CompanionAI][client] malformed chunk (anchor)");
                return null;
            }

            return new Input
            {
                msgId = l_msgId, seq = l_seq, total = l_total, n = l_n, status = f[4], ax = l_ax, ay = l_ay, az = l_az,
                payload = f[7]
            };
        }
    }

    // World の参照があちこちにある
    // 一か所に固められる段取りが付けばこのクラスを外に出す
    private static class WorldInfo
    {
        internal static GameManager GameManager;
        internal static World World;
        internal static float Now;

        internal static bool GameManagerIsNull => GameManager == null;
        internal static bool WorldIsNull => World == null;
        internal static bool WorldIsRemote => World.IsRemote();

        internal static void FillGameManager()
        {
            GameManager = GameManager.Instance;
        }

        internal static void FillWorld()
        {
            World = GameManager != null ? GameManager.World : null;
        }
    }
}