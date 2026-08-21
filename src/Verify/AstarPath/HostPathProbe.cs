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

namespace CompanionAIVerify.AstarPath
{
    // --- Tunables（検証後に Cfg / ModCfgFile へ昇格可。今はスライス隔離のためローカル保持） ------
    internal static class HostProbeCfg
    {
        internal static bool    Enabled                    = false;          // 起動時OFF。F10でトグル
        internal const  KeyCode ToggleKey                  = KeyCode.F10;     // F9 はゲーム側でスクリーンショットに割当のため回避（F8=FPS表示の前例よりバインド自体は読取を妨げないが、スクショ副作用を避ける）

        internal static string  CompanionName              = "";             // 空 = 最近傍の remote player を採用
        internal static bool    AssignNavigator            = true;           // コンパニオンに navigator を遅延付与（ルートA′）

        internal static float   RepathSec                  = 0.5f;           // 再発注の最小間隔
        internal static float   PathSpeed                  = 1.5f;           // FindPath へ渡す速度（抽出のみでは表示上の値）
        internal static float   PathTimeoutSec             = 3.0f;           // 発注後この秒数で結果が来なければ打ち切り
        internal static float   MaxCompanionDist           = 60.0f;          // これ以上離れたら発注しない。あえて緩く保つ:
                                                                   //   40〜59m の部分経路を辿って自力接近する自己修復帯域を殺さないため。
                                                                   //   安全弁は endGap 分類＋スタックタイマー側で持つ（下）。
        internal static float   LogThrottleSec             = 0.5f;           // 成功ログの最小間隔（状態/点数変化時は即時）

        // endGap 分類（到達判定）とスタック検出。部分経路を「捨てない・到達と同一視しない・状態信号にする」。
        internal static float   ReachEpsilonM              = 1.5f;           // endGap ≤ これ → REACHED（終端がリーダーに到達）
        internal static float   StuckMinDeltaM             = 0.5f;           // 進捗とみなす endGap の最小短縮量（ノイズ無視）
        internal static float   StuckSec                   = 3.0f;           // endGap が縮まないままこの秒数 → STUCK

        // 診断: フックが走っているか＆ゲート状態を無条件で吐く（Enabled/F9 と独立）。切り分け後は false へ。
        internal static bool    DebugHeartbeat             = true;
        internal static float   HeartbeatSec               = 2.0f;
    }

    internal static class WorldInfo
    {
        internal static World             World = null;
        internal static EntityPlayerLocal Leader    = null;
        internal static EntityPlayer      Companion = null;
        internal static float             Distance  = 0.0f;
        internal static float             Now       = 0.0f;

        internal static bool WorldIsNull => World == null;
        internal static bool LeaderIsNull => Leader == null;
        internal static bool CompanionIsNull => Companion == null;
        internal static bool WorldIsRemote => World.IsRemote();
        internal static bool AstarIsNull => AstarManager.Instance == null;
        internal static bool PathFinderThreadIsNull => PathFinderThread.Instance == null;

        internal static void FillLeader() => Leader = World.GetPrimaryPlayer();
        internal static void FillCompanion() => Companion = FindCompanion();
        internal static void MeasureDistance() => Distance = Vector3.Distance(Companion.position, Leader.position);

        // remote ( = ホスト非ローカル ) な生存プレイヤーからコンパニオンを選ぶ
        private static EntityPlayer FindCompanion()
        {
            var players       = World.GetPlayers();
            if (players == null) return null;

            string want       = HostProbeCfg.CompanionName;
            EntityPlayer best = null;
            float bestSq      = float.MaxValue;

            for (int i = 0; i < players.Count; i++)
            {
                EntityPlayer p = players[i];
                if (p == null || p == Leader) continue;
                if (!p.isEntityRemote) continue;                            // ホストのローカル(リーダー)を除外
                if (p.IsDead() || !p.IsSpawned()) continue;

                if (!string.IsNullOrEmpty(want))
                {
                    if (p.PlayerDisplayName == want) return p;               // 名前一致を最優先
                    continue;
                }

                float sq = (p.position - Leader.position).sqrMagnitude;
                if (sq < bestSq) { bestSq = sq; best = p; }
            }
            return best;
        }
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
        private static int    _companionId       = int.MinValue;
        private static bool   _awaitingResult    = false;
        private static float  _requestTime       = 0f;
        private static float  _nextRequestTime   = 0f;

        // endGap 分類 / スタック検出のエピソード状態
        private static float  _bestEndGap        = float.MaxValue;  // この接近エピソードで観測した最小 endGap（進捗の基準線）
        private static float  _lastProgressTime  = 0f;              // 最後に endGap が有意に縮んだ時刻
        private static string _lastStatus        = "";             // 直近ログした状態（変化時は即ログ）

        // ログ絞り込み
        private static int    _lastLoggedN       = int.MinValue;
        private static float  _nextLogTime       = 0f;
        private static float  _nextSkipLogTime   = 0f;
        private static float  _nextHeartbeat     = 0f;

        internal static void OnHostTick(EntityPlayerLocal self)
        {
            LogHeartbeat(self);
            LogEdge();

            Toggle();

            if (!HostProbeCfg.Enabled) return;

            WorldInfo.World = (GameManager.Instance != null) ? GameManager.Instance.World : null;
            if (WorldInfo.WorldIsNull) return;

            if (!CanRun()) return;

            ResetCompanionIfChanged();

            WorldInfo.MeasureDistance();
            if (!CompanionIsCloseToNavigate()) return;

            if (!EnsureNavigator()) return;                        // 付与不可なら発注しない

            WorldInfo.Now = Time.time;

            Order();

            Retrieve();
        }

        // F10 トグル ( ホストで押す想定。IsRemote 側で押しても実処理は下のゲートで弾かれる )
        private static void Toggle()
        {
            if (!Input.GetKeyDown(HostProbeCfg.ToggleKey)) return;

            HostProbeCfg.Enabled = !HostProbeCfg.Enabled;
            Log.Out("[CompanionAI][host] path-probe = " + HostProbeCfg.Enabled);
            if (!HostProbeCfg.Enabled) ResetState();
        }

        private static bool CanRun()
        {
            // --- ホスト自己ゲート ( サーバ側であること かつ A* が稼働していること が前提 ) --------------------------------------
            if (WorldInfo.WorldIsRemote) return false;                    // client では何もしない
            if (WorldInfo.AstarIsNull) return false;                      // A* 未稼働 ( 保険 )
            if (WorldInfo.PathFinderThreadIsNull) return false;
            WorldInfo.FillLeader();
            if (WorldInfo.LeaderIsNull) return false;
            WorldInfo.FillCompanion();
            if (WorldInfo.CompanionIsNull)
            {
                if (Time.time >= _nextSkipLogTime)
                {
                    _nextSkipLogTime = Time.time + 2.0f;
                    Log.Out("[CompanionAI][host] no companion (remote player) found");
                }
                return false;
            }
            return true;
        }

        // コンパニオンが変わったら状態リセット
        private static void ResetCompanionIfChanged()
        {
            if (WorldInfo.Companion.entityId == _companionId) return;

            _companionId = WorldInfo.Companion.entityId;
            _awaitingResult = false;
            _nextRequestTime = 0f;
            _lastLoggedN = int.MinValue;
            _bestEndGap = float.MaxValue;
            _lastProgressTime = Time.time;
            _lastStatus = string.Empty;
        }

        private static bool CompanionIsCloseToNavigate()
        {
            if (WorldInfo.Distance > HostProbeCfg.MaxCompanionDist)
            {
                if (Time.time >= _nextSkipLogTime)
                {
                    _nextSkipLogTime = Time.time + 2.0f;
                    Log.Out($"[CompanionAI][host] companion too far ({WorldInfo.Distance:0.0}m > {HostProbeCfg.MaxCompanionDist:0}m); skip request");
                }
                return false;
            }
            return true;
        }

        private static bool EnsureNavigator()
        {
            if (!HostProbeCfg.AssignNavigator) return false;
            if (WorldInfo.Companion.navigator == null)
            {
                // CreateNavigator(EntityAlive) : companion は EntityPlayer : EntityAlive
                WorldInfo.Companion.navigator = AstarManager.CreateNavigator(WorldInfo.Companion);
                Log.Out($"[CompanionAI][host] assigned navigator to companion '{WorldInfo.Companion.PlayerDisplayName}' ({WorldInfo.Companion.entityId})");
                return false;
            }
            return true;
        }

        // --- 発注 -----------------------------------------------------------------------
        private static void Order()
        {
            if (_awaitingResult) return;
            if (WorldInfo.Now < _nextRequestTime) return;

            // follow : コンパニオン -> リーダー
            // FindPath(entity, target, speed, canBreak, aiTask) : zombie と同一入口
            PathFinderThread.Instance.FindPath(WorldInfo.Companion, WorldInfo.Leader.position, HostProbeCfg.PathSpeed, false, null);

            _awaitingResult = true;
            _requestTime = WorldInfo.Now;
            _nextRequestTime = WorldInfo.Now + HostProbeCfg.RepathSec;
        }

        // --- 引取 -----------------------------------------------------------------------
        private static void Retrieve()
        {
            if (!_awaitingResult) return;

            PathInfo pi = PathFinderThread.Instance.GetPath(WorldInfo.Companion.entityId);  // path != null のときだけ非 null で返り、内部から除去される
            if (pi != null && pi.path != null)
            {
                ExtractAndLog(WorldInfo.Companion, WorldInfo.Leader, pi.path);
                pi.path.Destruct();                                     // ★プール返却。以後 points を参照しない
                _awaitingResult = false;
            }
            else if (!PathFinderThread.Instance.IsCalculatingPath(WorldInfo.Companion.entityId))
            {
                // worker が処理済みで経路なし ( path == null -> finishedPaths から除去済み )
                LogNoPath(WorldInfo.Companion, WorldInfo.Leader, WorldInfo.Distance, "no path (null result)");
                _awaitingResult = false;
            }
            else if (WorldInfo.Now - _requestTime > HostProbeCfg.PathTimeoutSec)
            {
                // 念のためのウォッチドッグ ( 詰まり )
                LogNoPath(WorldInfo.Companion, WorldInfo.Leader, WorldInfo.Distance, $"timeout {HostProbeCfg.PathTimeoutSec:0.0}s");
                _awaitingResult = false;
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

            // --- endGap 分類（到達 / 接近中 / スタック）------------------------------------------
            //   REACHED     : endGap ≤ ε。終端がリーダーに到達。完全経路。
            //   APPROACHING : endGap > ε だが有意に縮んでいる（＝部分経路を辿って自己修復中）。辿ってよい。
            //   STUCK       : endGap > ε かつ StuckSec 秒 縮まない。局所最小/追いつけない/袋小路。要エスカレーション。
            //   進捗の基準は「このエピソードで観測した最小 endGap(_bestEndGap)」。ノイズ±で誤リセットしない。
            float now = Time.time;
            string status;
            if (endGap <= HostProbeCfg.ReachEpsilonM)
            {
                status = "REACHED";
                _bestEndGap = float.MaxValue;   // エピソードを閉じる（次に離れたら新規接近として再スタート）
                _lastProgressTime = now;
            }
            else
            {
                if (endGap < _bestEndGap - HostProbeCfg.StuckMinDeltaM)
                {
                    _bestEndGap = endGap;       // 有意に前進 → 基準線更新
                    _lastProgressTime = now;
                    status = "APPROACHING";
                }
                else if (now - _lastProgressTime > HostProbeCfg.StuckSec)
                {
                    status = "STUCK";
                }
                else
                {
                    status = "APPROACHING";     // 猶予内（まだスタック判定しない）
                }
            }

            // スライス2: 抽出した経路＋状態をコンパニオンのクライアントへ送出（送信側で独立スロットル）
            PathWire.MaybeSendFromHost(companion, wps, status);

            // 絞り込み: 状態変化 or 点数変化は即時、それ以外は時間ゲート
            bool changed = (status != _lastStatus) || (n != _lastLoggedN);
            if (!changed && now < _nextLogTime) return;
            _lastStatus = status;
            _lastLoggedN = n;
            _nextLogTime = now + HostProbeCfg.LogThrottleSec;

            float sinceProgress = now - _lastProgressTime;
            Log.Out(
                $"[CompanionAI][host] path {status} comp='{companion.PlayerDisplayName}'({companion.entityId}) " +
                $"pts={n} straight={straight:0.0}m poly={poly:0.0}m endGap={endGap:0.0}m " +
                $"bestGap={(_bestEndGap == float.MaxValue ? 0f : _bestEndGap):0.0}m sinceProg={sinceProgress:0.0}s " +
                $"start=({start.x:0.0},{start.y:0.0},{start.z:0.0}) end=({end.x:0.0},{end.y:0.0},{end.z:0.0})");
        }

        private static void LogNoPath(EntityPlayer companion, EntityPlayer leader, float dist, string reason)
        {
            if (Time.time < _nextLogTime) return;
            _nextLogTime = Time.time + HostProbeCfg.LogThrottleSec;
            _lastLoggedN = int.MinValue;
            _lastStatus = "FAIL";
            Log.Out($"[CompanionAI][host] path FAIL comp={companion.entityId} d={dist:0.0}m : {reason} " +
                    "(グリッド76m外 / 未接続 / 生成失敗のいずれか)");
        }

        private static void ResetState()
        {
            _awaitingResult = false;
            _nextRequestTime = 0f;
            _lastLoggedN = int.MinValue;
            _bestEndGap = float.MaxValue;
            _lastProgressTime = Time.time;
            _lastStatus = "";
            // 付与した navigator はあえて残す（player では inert。探索中の null 化による worker NRE を避ける）。
        }

        // --- 診断ハートビート : この postfix が走っているか & ゲート状態を無条件で吐く ---------
        //   - 全く出ない                   -> フック未発火 ( DLL 未ロード / 未パッチ, または local player 不在 )
        //   - isRemote = true             -> この機は client である。実サーバは別インスタンス ( 計画の前提が崩れている )
        //   - astar = false / pft = false -> A* 未稼働 ( サーバだが探索器が立っていない )
        //   - primary = false             -> local player 未取得 ( この tick では発注不可 )
        private static void LogHeartbeat(EntityPlayerLocal self)
        {
            if (!HostProbeCfg.DebugHeartbeat) return;
            if (Time.time < _nextHeartbeat) return;

            _nextHeartbeat = Time.time + HostProbeCfg.HeartbeatSec;
            World hw         = (GameManager.Instance != null) ? GameManager.Instance.World : null;
            bool  isRemote   = (hw != null) && hw.IsRemote();
            bool  hasPrimary = (hw != null) && (hw.GetPrimaryPlayer() != null);
            int   players    = (hw != null && hw.GetPlayers() != null) ? hw.GetPlayers().Count : -1;
            Log.Out(
                $"[CompanionAI][host] hook alive: self={(self != null ? self.entityId : -1)} " +
                $"world={(hw != null)} isRemote={isRemote} primary={hasPrimary} players={players} " +
                $"astar={(AstarManager.Instance != null)} pft={(PathFinderThread.Instance != null)} " +
                $"enabled={HostProbeCfg.Enabled} focus={Application.isFocused}"
            );
        }

        // 入力エッジ診断 : このフック文脈で GetKeyDown が生きているかを可視化する。
        //   - 任意キー押下でこの行が出る               -> Input エッジは有効。F9 だけ効かないならキー横取り。
        //   - どのキーでも出ない ( かつ focus = true ) -> この文脈で Input が読めない ... 別フック要。
        private static void LogEdge()
        {
            if (!HostProbeCfg.DebugHeartbeat) return;
            if (!Input.anyKeyDown) return;

            Log.Out("[CompanionAI][host] Input.anyKeyDown edge seen here");
        }
    }
}
