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

using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace CompanionAIVerify
{
    internal static class PathWire
    {
        // --- Tunables ---------------------------------------------------------------------------
        internal const  string Tag             = "~CAIP~";  // 自タグ。人間チャットと衝突しにくく bbcode 文字を含まない
        internal static bool   SendEnabled     = true;      // 送信の有効/無効
        internal static int    ChunkChars      = 350;       // 1メッセージあたり payload の最大文字数（点境界で分割）
        internal static float  SendThrottleSec = 1.5f;      // 送信の最小間隔（状態/点数変化時は即時）
        internal static float  RxStaleSec      = 5.0f;      // 未完了バッファの破棄猶予

        // enum メンバは添付enum実体で未確認。コンパイルで弾かれたらここだけ実enumに合わせて差し替え。
        //   EChatType.Global / EMessageSender.None は表示側の値で、本経路では抑止するため機能に影響しない。
        internal const EChatType      WireChatType   = EChatType.Global;
        internal const EMessageSender WireMsgSender  = EMessageSender.None;

        // --- 送信状態（ホスト側）----------------------------------------------------------------
        private static int   _msgId       = 0;
        private static float _nextSendTime = 0f;
        private static string _lastSig    = "";

        // ============================ ホスト送信 ============================
        internal static void MaybeSendFromHost(EntityPlayer companion, Vector3[] wps, string status)
        {
            if (!SendEnabled || companion == null || wps == null || wps.Length == 0) return;

            GameManager gm = GameManager.Instance;
            if (gm == null) return;
            World world = gm.World;
            if (world == null || world.IsRemote()) return;      // ホスト(サーバ)限定

            float now = Time.time;
            string sig = status + ":" + wps.Length;             // 状態 or 点数が変われば即送信
            if (sig == _lastSig && now < _nextSendTime) return;
            _lastSig = sig;
            _nextSendTime = now + SendThrottleSec;

            int msgId = ++_msgId;
            List<string> chunks = Encode(wps, companion.position, status, msgId);

            List<int> recipients = new List<int> { companion.entityId };
            for (int i = 0; i < chunks.Count; i++)
            {
                gm.ChatMessageServer(null, WireChatType, -1, chunks[i], recipients, WireMsgSender,
                                     GeneratedTextManager.BbCodeSupportMode.Supported);
            }

            Vector3 s = wps[0], e = wps[wps.Length - 1];
            Log.Out($"[CompanionAI][host] sent path msgId={msgId} status={status} pts={wps.Length} " +
                    $"chunks={chunks.Count} start=({s.x:0.0},{s.y:0.0},{s.z:0.0}) end=({e.x:0.0},{e.y:0.0},{e.z:0.0})");
        }

        private static List<string> Encode(Vector3[] wps, Vector3 anchor, string status, int msgId)
        {
            int ax = Mathf.RoundToInt(anchor.x * 100f);
            int ay = Mathf.RoundToInt(anchor.y * 100f);
            int az = Mathf.RoundToInt(anchor.z * 100f);

            // 各点を anchor 相対 cm 整数の "dx,dy,dz" にする
            List<string> ptStrs = new List<string>(wps.Length);
            for (int i = 0; i < wps.Length; i++)
            {
                int dx = Mathf.RoundToInt(wps[i].x * 100f) - ax;
                int dy = Mathf.RoundToInt(wps[i].y * 100f) - ay;
                int dz = Mathf.RoundToInt(wps[i].z * 100f) - az;
                ptStrs.Add(dx + "," + dy + "," + dz);
            }

            // 点境界で貪欲チャンク（1点が ChunkChars を超える場合も最低1点は入れる）
            List<string> payloads = new List<string>();
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < ptStrs.Count; i++)
            {
                int add = ptStrs[i].Length + (sb.Length > 0 ? 1 : 0);
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

            int total = payloads.Count;
            string anchorStr = ax + "," + ay + "," + az;
            List<string> msgs = new List<string>(total);
            for (int i = 0; i < total; i++)
            {
                msgs.Add(Tag + "|" + msgId + "|" + i + "|" + total + "|" + status + "|" +
                         wps.Length + "|" + anchorStr + "|" + payloads[i]);
            }
            return msgs;
        }

        // ============================ クライアント受信 ============================
        private class RxBuf
        {
            public int total, n, ax, ay, az;
            public string status;
            public string[] parts;
            public int got;
            public float firstSeen;
        }

        private static readonly Dictionary<int, RxBuf> _rx = new Dictionary<int, RxBuf>();

        internal static void OnChunkClient(string msg)
        {
            PruneStale();

            // Tag|msgId|seq|total|status|n|ax,ay,az|payload  → 8分割（payload に '|' は無い）
            string[] f = msg.Split(new char[] { '|' }, 8);
            if (f.Length < 8) { Log.Warning("[CompanionAI][client] malformed chunk (fields<8)"); return; }

            int msgId, seq, total, n;
            if (!int.TryParse(f[1], out msgId) || !int.TryParse(f[2], out seq) ||
                !int.TryParse(f[3], out total) || !int.TryParse(f[5], out n))
            {
                Log.Warning("[CompanionAI][client] malformed chunk (header parse)");
                return;
            }
            string status = f[4];
            string[] a = f[6].Split(',');
            int ax, ay, az;
            if (a.Length < 3 || !int.TryParse(a[0], out ax) || !int.TryParse(a[1], out ay) || !int.TryParse(a[2], out az))
            {
                Log.Warning("[CompanionAI][client] malformed chunk (anchor)");
                return;
            }
            string payload = f[7];

            RxBuf b;
            if (!_rx.TryGetValue(msgId, out b))
            {
                if (total <= 0) return;
                b = new RxBuf
                {
                    total = total, n = n, status = status, ax = ax, ay = ay, az = az,
                    parts = new string[total], got = 0, firstSeen = Time.time
                };
                _rx[msgId] = b;
            }
            if (seq >= 0 && seq < b.total && b.parts[seq] == null)
            {
                b.parts[seq] = payload;
                b.got++;
            }
            if (b.got < b.total) return;

            // 全チャンク到着 → 再結合
            _rx.Remove(msgId);

            StringBuilder joined = new StringBuilder();
            for (int i = 0; i < b.total; i++)
            {
                string p = b.parts[i];
                if (string.IsNullOrEmpty(p)) continue;
                if (joined.Length > 0) joined.Append(';');
                joined.Append(p);
            }

            List<Vector3> pts = new List<Vector3>();
            if (joined.Length > 0)
            {
                string[] pointStrs = joined.ToString().Split(';');
                for (int i = 0; i < pointStrs.Length; i++)
                {
                    string ps = pointStrs[i];
                    if (ps.Length == 0) continue;
                    string[] c = ps.Split(',');
                    int dx, dy, dz;
                    if (c.Length < 3 || !int.TryParse(c[0], out dx) || !int.TryParse(c[1], out dy) || !int.TryParse(c[2], out dz))
                    {
                        Log.Warning("[CompanionAI][client] malformed point '" + ps + "'");
                        continue;
                    }
                    pts.Add(new Vector3((b.ax + dx) / 100f, (b.ay + dy) / 100f, (b.az + dz) / 100f));
                }
            }

            Vector3 s = pts.Count > 0 ? pts[0] : Vector3.zero;
            Vector3 e = pts.Count > 0 ? pts[pts.Count - 1] : Vector3.zero;
            string verdict = (pts.Count == b.n) ? "OK" : "COUNT-MISMATCH";
            Log.Out($"[CompanionAI][client] recv path msgId={msgId} status={b.status} " +
                    $"pts={pts.Count}(exp {b.n}) {verdict} chunks={b.total} " +
                    $"start=({s.x:0.0},{s.y:0.0},{s.z:0.0}) end=({e.x:0.0},{e.y:0.0},{e.z:0.0})");

            // 受信した pts はスライス3（クライアント追従）の入力になる。ここではログのみ。
        }

        private static void PruneStale()
        {
            if (_rx.Count == 0) return;
            float now = Time.time;
            List<int> drop = null;
            foreach (var kv in _rx)
            {
                if (now - kv.Value.firstSeen > RxStaleSec)
                {
                    if (drop == null) drop = new List<int>();
                    drop.Add(kv.Key);
                }
            }
            if (drop != null)
                for (int i = 0; i < drop.Count; i++)
                {
                    Log.Warning($"[CompanionAI][client] dropped incomplete msgId={drop[i]} (got<total, stale)");
                    _rx.Remove(drop[i]);
                }
        }
    }
}
