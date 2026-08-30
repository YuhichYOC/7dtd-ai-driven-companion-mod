/*
*
* CombatDriver.cs
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

using UnityEngine;

namespace CompanionAIVerify
{
    // --- Combat (engage slice) ----------------------------------------------
    internal static class CombatDriver
    {
        private static bool               _attackPressed;      // 近接: press 中フラグ
        private static float              _nextEngageLogTime;
        private static bool               _fpvLogged;
        private static bool               _lastFpv;

        // 遠距離: press/release マイクロサイクルとリロード可視化用
        private static bool               _firePressed;        // 前フレームで press した→今フレーム release
        private static float              _nextFireTime;
        private static int                _lastMeta = int.MinValue;
        private static bool               _adsOn;              // ADS(サイト覗き)状態。変化時のみトグル
        private static float              _nextHoldLogTime;    // ホールド理由ログの throttle

        // シュータブル候補狙点の再利用バッファ（毎フレーム alloc 回避）
        private static readonly Vector3[] _candPts = new Vector3[3];
        private static readonly string[]  _candNm  = new string[3];

        // 交戦オーバーレイ。posture 決定の後に最後に呼ぶ（in-range 時の 3D エイムが
        // 平面 facing を同フレーム内で上書きするため）。
        internal static void OnCombatStep(EntityPlayerLocal self, in ThreatInfo threat)
        {
            if (!Cfg.CombatMode || !threat.Valid)
            {
                ReleaseIfPressed(self);
                ReleaseFireIfPressed(self);
                return;
            }

            float reach = GetAttackReach(self);
            float d     = Mathf.Sqrt(threat.DistSq);

            // ★ v0.7(A): 交戦距離に応じた武器自動切替。切替した frame は settle のため即 return。
            WeaponSelector.RefreshLoadout(self, force: false);
            if (Cfg.AutoWeaponSwitch && WeaponSelector.MaybeSwitch(self, d)) return;

            bool inRange = d <= reach + Cfg.ReachBuffer;

            bool isRanged;
            bool melee = IsMeleeHolding(self, out isRanged);

            // ★ (1) 交戦の手前で bFirstPersonView を実ログ確定（近接/遠距離とも）
            bool aboutToEngage = isRanged ? (d <= Cfg.RangedMaxEngageMeters) : inRange;
            if (aboutToEngage) ConfirmFirstPersonView(self);

            if (isRanged) // ★ (3) 遠距離: 発砲スライス
            {
                RangedStep(self, in threat, d);
                return;
            }

            if (!inRange)
            {
                ReleaseIfPressed(self);
                return;
            }

            // ★ (2) 近接交戦: 3Dエイム（ピッチ込み）→ press 駆動スイング
            FaceTarget3D(self, threat.Target);

            if (self.Attack(false)) // press。ケイデンスは canStartAttack の APM 律速が制御
            {
                _attackPressed = true;
                if (Time.time >= _nextEngageLogTime)
                {
                    _nextEngageLogTime = Time.time + Cfg.LogThrottleSec;
                    Log.Out($"[CompanionAI] engage: swing {threat.Kind} {threat.State} d={d:0.0}m reach={reach:0.0}m");
                }
            }
        }

        internal static void ReleaseIfPressed(EntityPlayerLocal self)
        {
            if (_attackPressed)
            {
                self.Attack(true); // release（スイングの後始末）
                _attackPressed = false;
            }
        }

        // 遠距離のトリガーも安全に開放（脅威消失/無効化/切替時に呼ぶ）。ADSも解除。
        internal static void ReleaseFireIfPressed(EntityPlayerLocal self)
        {
            if (_firePressed)
            {
                self.Attack(true);
                _firePressed = false;
            }
            SetAds(self, false);
        }

        // ADS(サイト覗き)状態を変化時のみ切替。AimingGun setter は FOV/animator/Actions[1] に
        // 副作用があるため冪等呼び出しを避ける。secondary action を持たない銃では発動しない。
        private static void SetAds(EntityPlayerLocal self, bool on)
        {
            if (on && !Cfg.AimDownSightsOnEngage) on = false;
            if (on && !CanAimDownSights(self)) on = false;
            if (on == _adsOn) return;
            _adsOn = on;
            self.AimingGun = on; // 拡散 hip(1.0)↔aiming(0.1) を切替(ItemActionRanged:748,1346)
        }

        // ADS 可否: secondary action(Actions[1]) と actionData[1] が存在すること。
        // (AimingGun setter が actionData[1] を直接参照＝境界外で例外になるためのガード)
        private static bool CanAimDownSights(EntityPlayerLocal self)
        {
            var inv = self.inventory;
            var hi  = inv != null ? inv.holdingItem : null;
            var hid = inv != null ? inv.holdingItemData : null;
            return hi != null && hi.Actions != null && hi.Actions.Length >= 2 && hi.Actions[1] != null
                && hid != null && hid.actionData != null && hid.actionData.Count >= 2;
        }

        // ★ 発砲ドライバ（v0.6.0）: hit検証＋シュータブル・ゲート＋フルオート連射。
        //   1) 射線が対象コライダーに届く狙点を探す（ResolveShootableAim）。届かなければホールド。
        //   2) 届く狙点へカメラをスナップ（視差/ラグ解消）＋ADS。
        //   3) フルオート(GetBurstCount==0)は press 保持で RPM 連射、セミ/バーストは press/release 単発。
        private static void RangedStep(EntityPlayerLocal self, in ThreatInfo threat, float d)
        {
            if (!Cfg.EnableRangedFire)
            {
                ReleaseFireIfPressed(self);
                if (d <= Cfg.RangedMaxEngageMeters && Time.time >= _nextEngageLogTime)
                {
                    _nextEngageLogTime = Time.time + 1.0f;
                    Log.Out($"[CompanionAI] engage: ranged holding within reach (d={d:0.0}m) — fire disabled.");
                }
                return;
            }

            if (d > Cfg.RangedMaxEngageMeters)
            {
                ReleaseFireIfPressed(self);
                return;
            }

            EntityAlive tgt = threat.Target;
            float headLift  = tgt.getHeadPosition().y - tgt.position.y;

            // カメラ実ワールド位置（=弾の原点）。視差補正の基準。
            Vector3 camWorld = (Cfg.AimFromCameraOrigin && self.playerCamera != null)
                ? self.playerCamera.transform.position + Origin.position
                : self.getHeadPosition();

            // ★ (1) 射線が対象に届く狙点を探す
            Vector3 aimPoint;
            string aimMode, bodyPart, reason;
            bool shootable;
            if (Cfg.RequireShootable)
            {
                shootable = ResolveShootableAim(self, tgt, camWorld, headLift,
                                                out aimPoint, out aimMode, out bodyPart, out reason);
            }
            else
            {
                // ゲート無効時は従来のハイブリッド狙点をそのまま採用（検証なし）
                bool useHead = headLift >= Cfg.HeadAimMinLift;
                aimPoint = useHead ? tgt.getHeadPosition() : tgt.position + Vector3.up * tgt.scaledExtent.y;
                aimMode  = useHead ? "head" : "center";
                bodyPart = "-"; reason = "ok"; shootable = true;
            }

            // body/視覚トラッキング（見た目の照準）はホールド中も維持
            SetAimRotation(self, aimPoint - self.getHeadPosition());

            if (!shootable) // ★ 撃たない：遮蔽/FF/空。理由をログしてホールド
            {
                ReleaseFireIfPressed(self);
                if (Time.time >= _nextHoldLogTime)
                {
                    _nextHoldLogTime = Time.time + Cfg.LogThrottleSec;
                    Log.Out($"[CompanionAI] hold: {threat.Kind} id={tgt.entityId} d={d:0.0}m reason={reason}");
                }
                return;
            }

            // ★ (2) 発砲準備：ADS＋カメラを狙点へスナップ
            Vector3 shotDir = aimPoint - camWorld;
            SetAds(self, true);
            if (Cfg.SnapCameraOnFire && self.playerCamera != null && shotDir.sqrMagnitude > 1e-6f)
            {
                self.playerCamera.transform.rotation =
                    Quaternion.LookRotation(shotDir.normalized, Vector3.up);
            }

            // ★ (3) フルオート判定して駆動
            bool fullAuto = Cfg.FullAutoHold && IsFullAuto(self);

            if (fullAuto)
            {
                int mag = GetHoldingMeta(self);
                if (mag == 0)
                {
                    // ★ v0.6.1 空リロード: リロードは release エッジ(bReleased)が要る
                    //   (ItemActionRanged:1236 `if (bReleased) … if (CanReload) requestReload`)。
                    //   フルオートは hold で離さないため bReleased が立たず自動リロードしない。
                    //   → 空の間だけ release→press を交互に打ってエッジを作り、リロードを発火させる。
                    //   (CanReload に ADS ゲートは無い＝ItemActionRanged:872。ADS 解除は不要)
                    if (_firePressed) { self.Attack(true);  _firePressed = false; } // release（bReleased立て）
                    else              { self.Attack(false); _firePressed = true;  } // press（empty→requestReload）
                    FireLog(self, threat, d, aimMode, bodyPart, true, GetHoldingMeta(self), mag);
                }
                else
                {
                    // 弾あり: トリガー保持で RPM 連射（Delay がケイデンスを律速）。離しは disengage 時のみ。
                    self.Attack(false);
                    _firePressed = true;
                    FireLog(self, threat, d, aimMode, bodyPart, true, GetHoldingMeta(self), mag);
                }
            }
            else
            {
                // セミ/バースト：press(N)→release(N+1) を FireInterval ごと
                if (_firePressed) { self.Attack(true); _firePressed = false; return; }
                if (Time.time < _nextFireTime) return;
                int before = GetHoldingMeta(self);
                self.Attack(false);
                _firePressed = true;
                _nextFireTime = Time.time + Cfg.RangedFireIntervalSec;
                FireLog(self, threat, d, aimMode, bodyPart, false, GetHoldingMeta(self), before);
            }
        }

        // 発砲/リロードのログ。Meta 差で実発砲を検出し、命中エンティティ(MinEventContext.Other)を突合。
        private static void FireLog(EntityPlayerLocal self, in ThreatInfo threat, float d,
                                    string aimMode, string bodyPart, bool fullAuto, int after, int before)
        {
            if (after < 0 || before < 0) return;
            if (after < before) // 実発砲
            {
                Entity hitE = self.MinEventContext != null ? self.MinEventContext.Other : null;
                string hitDesc = (hitE == null) ? "none"
                    : (hitE.entityId == threat.Target.entityId ? "TARGET" : "OTHER id=" + hitE.entityId);
                Log.Out($"[CompanionAI] fire: {threat.Kind} id={threat.Target.entityId} d={d:0.0}m mag={after} aim={aimMode}({bodyPart}) auto={(fullAuto ? "on" : "off")} ads={(self.AimingGun ? "on" : "off")} -> hit={hitDesc}");
            }
            else if (after == 0) { if (_lastMeta != 0) Log.Out("[CompanionAI] fire: empty — waiting for auto-reload."); }
            else if (_lastMeta >= 0 && after > _lastMeta) Log.Out($"[CompanionAI] reload: done, mag={after}");
            _lastMeta = after;
        }

        // ★ シュータブル解決: 候補狙点(頭/胴中心/腹)を順に自前レイキャストし、
        //   対象コライダーに実際に当たる最初の点を返す。全滅なら理由(block/OTHER/sky)付きで false。
        //   fireShot と同じ SetModelLayer(2)＋Voxel.Raycast(world,ray,range,-538751005,8,0) を使用。
        private static bool ResolveShootableAim(EntityPlayerLocal self, EntityAlive tgt, Vector3 camWorld,
                                                float headLift, out Vector3 aimPoint, out string mode,
                                                out string part, out string reason)
        {
            aimPoint = tgt.position; mode = "none"; part = "-"; reason = "sky";

            Vector3 head   = tgt.getHeadPosition();
            Vector3 center = tgt.position + Vector3.up * tgt.scaledExtent.y;
            Vector3 belly  = tgt.getBellyPosition();
            if (headLift >= Cfg.HeadAimMinLift) { _candPts[0] = head;   _candNm[0] = "head";   _candPts[1] = center; _candNm[1] = "center"; }
            else                                { _candPts[0] = center; _candNm[0] = "center"; _candPts[1] = head;   _candNm[1] = "head";   }
            _candPts[2] = belly; _candNm[2] = "belly";

            World world = self.world;
            bool haveReason = false;
            int ml = self.GetModelLayer();
            self.SetModelLayer(2); // 自己を射線から除外（fireShot と同じ）
            try
            {
                for (int i = 0; i < 3; i++)
                {
                    Vector3 dir = _candPts[i] - camWorld;
                    if (dir.sqrMagnitude < 1e-6f) continue;
                    float range = dir.magnitude + 1.0f;
                    if (!Voxel.Raycast(world, new Ray(camWorld, dir.normalized), range, -538751005, 8, 0f))
                    {
                        if (!haveReason) { haveReason = true; reason = "sky"; }
                        continue;
                    }
                    WorldRayHitInfo info = Voxel.voxelRayHitInfo.Clone();
                    Entity e = ItemActionAttack.FindHitEntityNoTagCheck(info, out string bp);
                    if (e != null && e.entityId == tgt.entityId)
                    {
                        aimPoint = _candPts[i]; mode = _candNm[i]; part = string.IsNullOrEmpty(bp) ? "body" : bp; reason = "ok";
                        return true;
                    }
                    if (!haveReason)
                    {
                        haveReason = true;
                        if (e != null) reason = "OTHER id=" + e.entityId;
                        else reason = "block:" + (string.IsNullOrEmpty(info.tag)
                            ? (info.transform != null ? info.transform.name : "?") : info.tag);
                    }
                }
            }
            finally { self.SetModelLayer(ml); }
            return false;
        }

        // フルオート判定: GetBurstCount==0（BurstRoundCount 既定1=セミ, 0=フル, N=バースト）。
        private static bool IsFullAuto(EntityPlayerLocal self)
        {
            var inv = self.inventory;
            var hi  = inv != null ? inv.holdingItem : null;
            var hid = inv != null ? inv.holdingItemData : null;
            if (hi == null || hi.Actions == null || hi.Actions.Length == 0) return false;
            var ra = hi.Actions[0] as ItemActionRanged;
            if (ra == null || hid == null || hid.actionData == null || hid.actionData.Count == 0) return false;
            return ra.GetBurstCount(hid.actionData[0]) == 0;
        }

        // 保持中アイテムの装填残弾(Meta)。取得不可は -1。 (A4: holdingItemItemValue.Meta)
        private static int GetHoldingMeta(EntityPlayerLocal self)
        {
            var inv = self.inventory;
            var iv  = inv != null ? inv.holdingItemItemValue : null;
            return iv != null ? iv.Meta : -1;
        }

        // 意図方向(aimDir)で body/camera を向ける。ranged ショットは GetLookRay(camera) 由来なので
        // SetRotation がカメラ Angle を更新する（ItemActionRanged:1579, EPL:2310）。
        // ただしカメラ transform 反映は遅延するため、ラグ対策は RangedStep 側で別途スナップする。
        private static void SetAimRotation(EntityPlayerLocal self, Vector3 aimDir)
        {
            if (aimDir.sqrMagnitude < 1e-6f) return;
            Vector3 euler = Quaternion.LookRotation(aimDir.normalized, Vector3.up).eulerAngles;
            euler.x *= -1f; // ピッチ反転（EPL:239, 251）
            self.SetRotation(euler);
        }

        // 保持アイテム action[0] の射程。取れなければ素手相当 2.0m。
        private static float GetAttackReach(EntityPlayerLocal self)
        {
            var hi = self.inventory != null ? self.inventory.holdingItem : null;
            var a  = (hi != null && hi.Actions != null && hi.Actions.Length > 0) ? hi.Actions[0] : null;
            if (a != null && a.Range > 0.01f) return a.Range;
            return 2.0f;
        }

        // 近接か遠距離か。ItemActionRanged のみ遠距離扱い、それ以外(素手/工具/近接武器)は近接。
        private static bool IsMeleeHolding(EntityPlayerLocal self, out bool isRanged)
        {
            var hi = self.inventory != null ? self.inventory.holdingItem : null;
            var a  = (hi != null && hi.Actions != null && hi.Actions.Length > 0) ? hi.Actions[0] : null;
            isRanged = a is ItemActionRanged;
            return !isRanged;
        }

        // 交戦の手前で bFirstPersonView を実ログ。初回 or 変化時のみ出力。
        private static void ConfirmFirstPersonView(EntityPlayerLocal self)
        {
            bool fpv = self.bFirstPersonView;
            if (_fpvLogged && fpv == _lastFpv) return;

            _fpvLogged = true;
            _lastFpv   = fpv;
            Log.Out($"[CompanionAI] engage-precheck: bFirstPersonView={fpv} TPCam={self.TPCameraCheckResult} camPassed={self.TPCameraCheckPassed}");

            if (!fpv && Cfg.ForceFirstPerson)
            {
                self.SetFirstPersonView(true, false); // spawn経路の誤設定を自己修復
                Log.Out("[CompanionAI] engage-precheck: forced bFirstPersonView=true (ForceFirstPerson).");
            }
        }

        // ピッチ込みで対象中心付近を狙う（低い脅威にも当てるため y を潰さない）。
        // 変換式は facing と同一（EPL:2310, 248-252）。camera 経由で攻撃レイが操舵される。
        private static void FaceTarget3D(EntityPlayerLocal self, EntityAlive target)
        {
            Vector3 eye = self.position + Vector3.up * 1.5f;   // 概算カメラ高
            Vector3 aim = target.position + Vector3.up * 0.9f; // 概算胴中心
            Vector3 dir = aim - eye;
            if (dir.sqrMagnitude < 1e-6f) return;

            Vector3 euler = Quaternion.LookRotation(dir.normalized, Vector3.up).eulerAngles;
            euler.x *= -1f; // ピッチ反転（EPL:239, 251）
            self.SetRotation(euler);
        }
    }
}
