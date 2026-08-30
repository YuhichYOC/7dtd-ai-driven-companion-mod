/*
*
* HostPathProbe.cs
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

// =============================================================================
// navigation スライス1 : ホスト側 経路生成＋抽出（ログのみ・送信なし）
//
// ねらい:
//   リーダー側(=ホスト)で、コンパニオン(remote EntityPlayer)に対して vanilla の
//   経路探索を発注し、結果ウェイポイント列を取り出せることをログで検証する。
//   まだクライアントへは送らない。このファイル単独で閉じる検証ハーネス。
//
// 確定した設計根拠（監査 2026-08-18b / ルートA′。file:line は 3.1.0 逆コンパイル実物）:
//   - A* 一式はサーバ専用。AstarManager.Instance は client で null。
//       AstarManager.Init … IsServer ガード (AstarManager:172-179)
//     → 本コンポーネントは「!world.IsRemote() かつ AstarManager.Instance!=null」で自己ゲート。
//   - 追従グリッドは全プレイヤーに配置される（コンパニオン周囲も常に被覆）。
//       UpdateGraphs が world.Players.list を走査し Merge(pos,76) (AstarManager:366-374)
//       グリッド実寸 76m 四方 @1m (cGridXZSize=76 :96 / SetDimensions :745)
//   - 探索能力はグリッド固定＝ゾンビ相当 (maxClimb=1.3 :746 / maxSlope=60 :747)。
//   - 経路探索は entity.navigator 経由で走る。
//       worker: pathInfo.entity.navigator.GetPathTo(pathInfo) (AStarPathFinderThread:80)
//     player は navigator=null (EntityAlive:2056-2059) なので素の FindPath は NRE。
//     → コンパニオンに navigator を1個だけ遅延付与する（ルートA′）。
//        navigator は public フィールド (EntityAlive:704)
//        CreateNavigator は public static、内部で ASPPathNavigate を返す (AstarManager:197-200)
//   - canNavigate() は接地中の player で true。
//       PathNavigate.canNavigate → EntityAlive.CanNavigatePath (PathNavigate:86 / EntityAlive:3164-3171)
//       （onGround||isSwimming||bInElevator で true。EntityPlayer/Local に override 無し）
//   - navigator を付与しても player は自走しない（安全）。
//       updateTasks() は !IsClientControlled() ゲート (EntityAlive:3398)
//       EntityPlayer.IsClientControlled()==true (EntityPlayer:1179) → 駆動ループは player で走らない。
//       → navigator は我々が明示的に FindPath/GetPath を呼んだときだけ使われる。
//   - 発注/引取は zombie と同一 API。
//       FindPath (AStarPathFinderThread:111) / GetPath (:124) / IsCalculatingPath (:103)
//   - 送信ペイロードの正体 = PathEntity.points[].projectedLocation。
//       PathEntity.points : public PathPoint[] (PathEntity:7)
//       PathPoint.projectedLocation : public Vector3、絶対ワールド座標
//         （pathFollow が theEntity.position と直接比較 ASPPathNavigate:93-95）
//       ProjectToGround はこのビルドで no-op（projectedLocation を返すだけ PathPoint:96-99）
//   - プール返却が必須。PathPoint はプール割当、PathEntity.Destruct が全点 Release (PathEntity:34-42)。
//       → 同tickで projectedLocation をコピー→即 Destruct。PathEntity/PathPoint を跨tick保持しない。
//
// 実行モデル:
//   フック = EntityPlayerLocal.MoveByInput の prefix。
//     ホストでは local player = リーダー。毎フレーム発火し、既存F8と同じ入力実績のある site。
//     （当初 OnUpdateLive postfix を使ったが、その文脈では GetKeyDown のエッジを取りこぼしたため移設）
//     コンパニオンPCでも発火するが !world.IsRemote() で弾く。既存 OnMovePrefix とは別prefixで共存。
//   リーダー = world.GetPrimaryPlayer() (World:854)
//   コンパニオン = remote な EntityPlayer (isEntityRemote==true, Entity:258)。
//     名前指定があれば PlayerDisplayName 一致 (EntityPlayer:257)、無ければ最近傍。
//   目標 = リーダー現在地（follow ユースケースそのもの。経路の妥当性を目視検証しやすい）。
//
// 本スライスの範囲（意図的に絞る）:
//   - ログ出力のみ。Vector3[] 抽出まで行うが送信はしない（送信路はスライス2）。
//   - 追従（MoveByInput 駆動）はしない（スライス3）。
//   - 単一コンパニオン想定。複数時は CompanionName で指定。
//
// 導入:
//   本 MOD を LEADER(ホスト)PC にも導入する。PatchAll が本パッチを自動登録。
//   ホストで F10 = 本プローブの ON/OFF（既定OFF）。既存の F8 コンパニオンドライバとは独立。
//   （F9 はゲーム側でスクリーンショットに割当のため回避。バインド自体は Input.GetKeyDown を
//     妨げない＝F8=FPS表示の前例で実証済みだが、スクショ副作用を避けるため別キーにする。）
//   ※ホスト側では F8(コンパニオン駆動) は OFF のままにすること（リーダー自身を動かさないため）。
//
// 参照DLL: Assembly-CSharp.dll / UnityEngine.CoreModule.dll / 0Harmony.dll
// =============================================================================

using GamePath;
using HarmonyLib;
using UnityEngine;

namespace CompanionAIVerify
{
    // --- Tunables（検証後に Cfg / ModCfgFile へ昇格可。今はスライス隔離のためローカル保持） ------
    internal static class HostProbeCfg
    {
        internal static bool    Enabled          = false;          // 起動時OFF。F10でトグル
        internal const  KeyCode ToggleKey        = KeyCode.F10;     // F9 はゲーム側でスクリーンショットに割当のため回避（F8=FPS表示の前例よりバインド自体は読取を妨げないが、スクショ副作用を避ける）

        internal static string  CompanionName    = "";             // 空 = 最近傍の remote player を採用
        internal static bool    AssignNavigator  = true;           // コンパニオンに navigator を遅延付与（ルートA′）

        internal static float   RepathSec        = 0.5f;           // 再発注の最小間隔
        internal static float   PathSpeed        = 1.5f;           // FindPath へ渡す速度（抽出のみでは表示上の値）
        internal static float   PathTimeoutSec   = 3.0f;           // 発注後この秒数で結果が来なければ打ち切り
        internal static float   MaxCompanionDist = 60.0f;          // これ以上離れたら発注しない（グリッド外の空振り回避）
        internal static float   LogThrottleSec   = 0.5f;           // 成功ログの最小間隔（点数変化時は即時）

        // 診断: フックが走っているか＆ゲート状態を無条件で吐く（Enabled/F9 と独立）。切り分け後は false へ。
        internal static bool    DebugHeartbeat   = true;
        internal static float   HeartbeatSec     = 2.0f;
    }

    // --- Harmony patch : ホスト側の毎tickティック源 ---------------------------------------------
    //   入力(F9)を確実に拾うため、既存F8と同じ MoveByInput prefix を使う。
    //   OnUpdateLive postfix では GetKeyDown のフレームエッジを取りこぼす個体があるため移設。
    //   ホストでは local player(リーダー)に対し毎フレーム呼ばれる。companion PC でも走るが
    //   OnHostTick 内の !IsRemote ゲートで弾かれる。既存 OnMovePrefix とは別prefixとして共存。
    [HarmonyPatch(typeof(EntityPlayerLocal), "MoveByInput")]
    internal static class Patch_EntityPlayerLocal_MoveByInput_HostProbe
    {
        private static void Prefix(EntityPlayerLocal __instance)
        {
            HostPathProbe.OnHostTick(__instance);
        }
    }

    // --- Probe ---------------------------------------------------------------------------------
    internal static class HostPathProbe
    {
        // 単一コンパニオン想定の逐次状態
        private static int   _companionId       = int.MinValue;
        private static bool  _awaitingResult    = false;
        private static float _requestTime       = 0f;
        private static float _nextRequestTime   = 0f;

        // ログ絞り込み
        private static int   _lastLoggedN       = int.MinValue;
        private static float _nextLogTime       = 0f;
        private static float _nextSkipLogTime   = 0f;
        private static float _nextHeartbeat     = 0f;

        internal static void OnHostTick(EntityPlayerLocal self)
        {
            // --- 診断ハートビート: この postfix が走っているか＆ゲート状態を無条件で吐く ---------
            //   ・全く出ない  → フック未発火（DLL未ロード/未パッチ、または local player 不在）
            //   ・isRemote=true → この機は client。実サーバは別インスタンス（計画の前提が崩れている）
            //   ・astar=false / pft=false → A* 未稼働（サーバだが探索器が立っていない）
            //   ・primary=false → local player 未取得（この tick では発注不可）
            if (HostProbeCfg.DebugHeartbeat && Time.time >= _nextHeartbeat)
            {
                _nextHeartbeat = Time.time + HostProbeCfg.HeartbeatSec;
                World hw = (GameManager.Instance != null) ? GameManager.Instance.World : null;
                bool isRemote  = (hw != null) && hw.IsRemote();
                bool hasPrimary= (hw != null) && (hw.GetPrimaryPlayer() != null);
                int  players   = (hw != null && hw.GetPlayers() != null) ? hw.GetPlayers().Count : -1;
                Log.Out($"[CompanionAI][host] hook alive: self={(self != null ? self.entityId : -1)} " +
                        $"world={(hw != null)} isRemote={isRemote} primary={hasPrimary} players={players} " +
                        $"astar={(AstarManager.Instance != null)} pft={(PathFinderThread.Instance != null)} " +
                        $"enabled={HostProbeCfg.Enabled} focus={Application.isFocused}");
            }

            // 入力エッジ診断: このフック文脈で GetKeyDown が生きているかを可視化する。
            //   ・任意キー押下でこの行が出る → Input エッジは有効。F9 だけ効かないならキー横取り。
            //   ・どのキーでも出ない(かつ focus=True) → この文脈で Input が読めない → 別フック要。
            if (HostProbeCfg.DebugHeartbeat && Input.anyKeyDown)
                Log.Out("[CompanionAI][host] Input.anyKeyDown edge seen here");

            // F10 トグル（ホストで押す想定。IsRemote 側で押しても実処理は下のゲートで弾かれる）
            if (Input.GetKeyDown(HostProbeCfg.ToggleKey))
            {
                HostProbeCfg.Enabled = !HostProbeCfg.Enabled;
                Log.Out("[CompanionAI][host] path-probe = " + HostProbeCfg.Enabled);
                if (!HostProbeCfg.Enabled) ResetState();
            }
            if (!HostProbeCfg.Enabled) return;

            World world = (GameManager.Instance != null) ? GameManager.Instance.World : null;
            if (world == null) return;

            // --- ホスト自己ゲート（サーバ かつ A* 稼働）--------------------------------------
            if (world.IsRemote()) return;                                   // client では何もしない
            if (AstarManager.Instance == null) return;                      // A* 未稼働（保険）
            if (PathFinderThread.Instance == null) return;

            EntityPlayerLocal leader = world.GetPrimaryPlayer();
            if (leader == null) return;

            EntityPlayer companion = FindCompanion(world, leader);
            if (companion == null)
            {
                if (Time.time >= _nextSkipLogTime)
                {
                    _nextSkipLogTime = Time.time + 2.0f;
                    Log.Out("[CompanionAI][host] no companion (remote player) found");
                }
                return;
            }

            // コンパニオンが変わったら状態リセット
            if (companion.entityId != _companionId)
            {
                _companionId = companion.entityId;
                _awaitingResult = false;
                _nextRequestTime = 0f;
                _lastLoggedN = int.MinValue;
            }

            float dist = Vector3.Distance(companion.position, leader.position);
            if (dist > HostProbeCfg.MaxCompanionDist)
            {
                if (Time.time >= _nextSkipLogTime)
                {
                    _nextSkipLogTime = Time.time + 2.0f;
                    Log.Out($"[CompanionAI][host] companion too far ({dist:0.0}m > {HostProbeCfg.MaxCompanionDist:0}m); skip request");
                }
                return;
            }

            EnsureNavigator(companion);
            if (companion.navigator == null) return;                        // 付与不可なら発注しない

            float now = Time.time;

            // --- 発注 -----------------------------------------------------------------------
            if (!_awaitingResult && now >= _nextRequestTime)
            {
                Vector3 target = leader.position;                           // follow: コンパニオン→リーダー
                // FindPath(entity, target, speed, canBreak, aiTask) : zombie と同一入口
                PathFinderThread.Instance.FindPath(companion, target, HostProbeCfg.PathSpeed, false, null);
                _awaitingResult = true;
                _requestTime = now;
                _nextRequestTime = now + HostProbeCfg.RepathSec;
            }

            // --- 引取 -----------------------------------------------------------------------
            if (_awaitingResult)
            {
                PathInfo pi = PathFinderThread.Instance.GetPath(companion.entityId);  // path!=null のときだけ非nullで返り、内部から除去される
                if (pi != null && pi.path != null)
                {
                    ExtractAndLog(companion, leader, pi.path);
                    pi.path.Destruct();                                     // ★プール返却。以後 points を参照しない
                    _awaitingResult = false;
                }
                else if (!PathFinderThread.Instance.IsCalculatingPath(companion.entityId))
                {
                    // worker が処理済みで経路なし（path==null → finishedPaths から除去済み）
                    LogNoPath(companion, leader, dist, "no path (null result)");
                    _awaitingResult = false;
                }
                else if (now - _requestTime > HostProbeCfg.PathTimeoutSec)
                {
                    // 念のためのウォッチドッグ（詰まり）
                    LogNoPath(companion, leader, dist, $"timeout {HostProbeCfg.PathTimeoutSec:0.0}s");
                    _awaitingResult = false;
                }
            }
        }

        // remote(=ホスト非ローカル) な生存プレイヤーからコンパニオンを選ぶ
        private static EntityPlayer FindCompanion(World world, EntityPlayerLocal leader)
        {
            var players = world.GetPlayers();
            if (players == null) return null;

            string want = HostProbeCfg.CompanionName;
            EntityPlayer best = null;
            float bestSq = float.MaxValue;

            for (int i = 0; i < players.Count; i++)
            {
                EntityPlayer p = players[i];
                if (p == null || p == leader) continue;
                if (!p.isEntityRemote) continue;                            // ホストのローカル(リーダー)を除外
                if (p.IsDead() || !p.IsSpawned()) continue;

                if (!string.IsNullOrEmpty(want))
                {
                    if (p.PlayerDisplayName == want) return p;               // 名前一致を最優先
                    continue;
                }

                float sq = (p.position - leader.position).sqrMagnitude;
                if (sq < bestSq) { bestSq = sq; best = p; }
            }
            return best;
        }

        private static void EnsureNavigator(EntityPlayer companion)
        {
            if (!HostProbeCfg.AssignNavigator) return;
            if (companion.navigator == null)
            {
                // CreateNavigator(EntityAlive) : companion は EntityPlayer : EntityAlive
                companion.navigator = AstarManager.CreateNavigator(companion);
                Log.Out($"[CompanionAI][host] assigned navigator to companion '{companion.PlayerDisplayName}' ({companion.entityId})");
            }
        }

        private static void ExtractAndLog(EntityPlayer companion, EntityPlayer leader, PathEntity pe)
        {
            PathPoint[] pts = pe.points;
            int n = (pts != null) ? pts.Length : 0;
            if (n == 0)
            {
                LogNoPath(companion, leader, Vector3.Distance(companion.position, leader.position), "path arrived but 0 points");
                return;
            }

            // ★スライスの成果物: ウェイポイントを Vector3[] へ落とす（この配列がスライス2の送信ペイロード）
            Vector3[] wps = new Vector3[n];
            for (int i = 0; i < n; i++) wps[i] = pts[i].projectedLocation;

            Vector3 start = wps[0];
            Vector3 end   = wps[n - 1];
            float straight = Vector3.Distance(companion.position, leader.position);
            float poly = 0f;
            for (int i = 1; i < n; i++) poly += Vector3.Distance(wps[i - 1], wps[i]);
            float endGap = Vector3.Distance(end, leader.position);           // 経路終端がリーダーへ届いているか

            // 絞り込み: 点数変化時は即時、それ以外は時間ゲート
            bool changed = (n != _lastLoggedN);
            if (!changed && Time.time < _nextLogTime) return;
            _lastLoggedN = n;
            _nextLogTime = Time.time + HostProbeCfg.LogThrottleSec;

            Log.Out(
                $"[CompanionAI][host] path OK comp='{companion.PlayerDisplayName}'({companion.entityId}) " +
                $"pts={n} straight={straight:0.0}m poly={poly:0.0}m endGap={endGap:0.0}m " +
                $"start=({start.x:0.0},{start.y:0.0},{start.z:0.0}) end=({end.x:0.0},{end.y:0.0},{end.z:0.0})");
        }

        private static void LogNoPath(EntityPlayer companion, EntityPlayer leader, float dist, string reason)
        {
            if (Time.time < _nextLogTime) return;
            _nextLogTime = Time.time + HostProbeCfg.LogThrottleSec;
            _lastLoggedN = int.MinValue;
            Log.Out($"[CompanionAI][host] path FAIL comp={companion.entityId} d={dist:0.0}m : {reason} " +
                    "(グリッド76m外 / 未接続 / 生成失敗のいずれか)");
        }

        private static void ResetState()
        {
            _awaitingResult = false;
            _nextRequestTime = 0f;
            _lastLoggedN = int.MinValue;
            // 付与した navigator はあえて残す（player では inert。探索中の null 化による worker NRE を避ける）。
        }
    }
}
