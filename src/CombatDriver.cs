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

using System.Collections.Generic;
using UnityEngine;

namespace CompanionAIVerify
{
    // --- Combat (engage slice) ----------------------------------------------
    internal static class CombatDriver
    {
        private static bool               _attackPressed;      // 近接: press 中フラグ
        private static bool               _aimAssistSet;       // v0.8(B)-A: SetAttackTarget を張ったか（解除用）
        private static float              _nextEngageLogTime;
        private static bool               _fpvLogged;
        private static bool               _lastFpv;

        // 遠距離: press/release マイクロサイクルとリロード可視化用
        private static bool               _firePressed;        // 前フレームで press した→今フレーム release
        private static float              _nextFireTime;
        private static int                _lastMeta = int.MinValue;
        private static bool               _adsOn;              // ADS(サイト覗き)状態。変化時のみトグル
        private static float              _nextHoldLogTime;    // ホールド理由ログの throttle

        // ★ [bow] 弓/クロスボウ(ItemActionCatapult)ドロー状態。
        //   press でチャージ開始→m_MaxStrainTime×BowDrawFraction 経過→release で発射。
        //   ゲーム側 m_bActivated / m_ActivateTime を「状態の信頼源」にして同期する（自前タイマーを二重に持たない）。
        private static bool               _bowDrawing;         // ドロー中（ゲーム側 m_bActivated と同期）
        private static float              _bowNextTry;         // 発射直後の再ドロー抑制（連射律速自体はゲーム Delay: Catapult:109 が担保）
        private static float              _nextBowLogTime;     // 弓ログ throttle

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

            EngageRange.LogTick(self, threat.Target);

            // ★ v0.7(A): 交戦距離に応じた武器自動切替。切替した frame は settle のため即 return。
            WeaponSelector.RefreshLoadout(self, force: false);
            if (Cfg.AutoWeaponSwitch && WeaponSelector.MaybeSwitch(self, d))
            {
                // ★ [bow] 切替で武器が変わる前に、押下中のトリガー/ドローを安全開放する。
                //   （弓ドロー中は release=発射になるため、ここを通さないと切替の瞬間に暴発しうる）。
                ReleaseFireIfPressed(self);
                return;
            }

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

            // ★ v0.8(B)-A: 近接レイをターゲットのチェストへ自動補正させる。
            //   ItemActionDynamic.GetExecuteActionTarget は attackTarget!=null のとき
            //   ray を getChestPosition() 方向へ差し替える（ItemActionDynamic:327-330）。
            //   これで FaceTarget3D の平面精度に依存せず命中が安定する。
            //
            //   ※ client では SetAttackTarget() を使えない：内部で world.entityDistributer.SendPacket を叩くが
            //     entityDistributer は IsServer 時のみ生成される（World:468-477）ため client では null → NRE。
            //     さらに attackTargetTime>0 にすると自動失効パス(EntityAlive:3367-3376)も同じ null を踏む。
            //     → public フィールドへ直接代入し、attackTargetTime は 0 のまま（失効パスに入らせない）。
            //       解除は ReleaseIfPressed で直接 null 代入。redirect は attackTarget を読むだけ(GetAttackTarget:5890)なので十分。
            //       ダメージ同期は Attack()→DamageEntity()→NetPackageDamageEntity 経由で別途成立（attackTarget 非依存）。
            if (Cfg.MeleeAimAssist)
            {
                self.attackTarget = threat.Target; // EntityAlive:716 (public field) — client-safe
                _aimAssistSet = true;
            }

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
            // v0.8(B)-A: 張っていた aim-assist の attackTarget を解除。
            //   client では SetAttackTarget(null,0) も entityDistributer.SendPacket(EntityAlive:5932)で NRE になるため
            //   フィールドを直接 null 代入する。attackTargetTime は元々 0 のまま＝失効パス(3367-)にも入らない。
            if (_aimAssistSet)
            {
                self.attackTarget = null; // client-safe な直接解除
                _aimAssistSet = false;
            }
        }

        // 遠距離のトリガーも安全に開放（脅威消失/無効化/切替時に呼ぶ）。ADSも解除。
        internal static void ReleaseFireIfPressed(EntityPlayerLocal self)
        {
            // ★ [bow] 弓ドロー中は release=発射になる。開放要求は「キャンセル（矢を消費しない引き戻し）」へ
            //   振り替えて暴発を防ぐ。CancelAction は m_bActivated 中に triggerReleased して活性を落とす
            //   （矢は ConsumeAmmo を通らないため消費されない, ItemActionCatapult:176-196）。
            if (_bowDrawing)
            {
                var cat = GetHeldCatapult(self, out var cdata);
                if (cat != null && cdata != null) cat.CancelAction(cdata);
                _bowDrawing = false; // 武器が既に切替済み(cat==null)でもフラグは必ず落とす（StopHolding が旧弓を CancelAction 済み）
            }
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
        //   3') ★ [bow] 弓(ItemActionCatapult)は press→ドロー保持→release の3相へ分岐（前段は共有）。
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

            // ★ v0.8(C): 射程ゲート。グローバル上限(RangedMaxEngageMeters)に加え、
            //   武器固有の実効射程でも「弾が届かない距離」を弾く。fireMax = min(グローバル上限, 実効射程×安全係数)。
            //   実効射程 = EngageRange.Read().range（ranged では GetRange()＝MaxRange 適用後の発射射程, ItemActionRanged:1376）。
            //   Slice A 実測で shotgun range≈10 なのに d≈20 で撃って弾が届かない問題を解消する。
            //   d は feet-to-feet、実際の弾は camera→aimPoint なので安全係数(既定0.85)で余裕を持たせる。
            //   ※ [bow] 弓も ItemActionRanged 派生なので GetRange が取れる。ただし矢は放物線弾道で直線射程とズレる
            //     （fireShot は無効化: Launcher:120-125）。落下/リードの弾道補正は本スライスのスコープ外。
            float fireMax = Cfg.RangedMaxEngageMeters;
            EngageRange.Info erC = EngageRange.Read(self);
            if (erC.valid && erC.isRanged && erC.range > 0.01f)
                fireMax = Mathf.Min(fireMax, erC.range * Cfg.RangedRangeSafety);

            if (d > fireMax)
            {
                ReleaseFireIfPressed(self);
                if (Time.time >= _nextHoldLogTime)
                {
                    _nextHoldLogTime = Time.time + Cfg.LogThrottleSec;
                    Log.Out($"[CompanionAI] hold: {threat.Kind} id={threat.Target.entityId} d={d:0.0}m > fireMax={fireMax:0.0}m " +
                            $"(range={erC.range:0.0} x{Cfg.RangedRangeSafety:0.00}, cap={Cfg.RangedMaxEngageMeters:0.0})");
                }
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
                ReleaseFireIfPressed(self); // [bow] ドロー中ならキャンセル（暴発回避）
                if (Time.time >= _nextHoldLogTime)
                {
                    _nextHoldLogTime = Time.time + Cfg.LogThrottleSec;
                    Log.Out($"[CompanionAI] hold: {threat.Kind} id={tgt.entityId} d={d:0.0}m reason={reason}");
                }
                return;
            }

            // ★ v0.8(D): 友軍射線ガード。実射(fireShot)と同一原点(GetLookRay)＋同一狙点方向で、
            //   対象より手前の射線帯に友軍（他プレイヤー＋allyドローン）が居れば、狙点が通っていても発砲しない。
            //   既存の shootable(狙点探索/遮蔽)は「頭が1点通れば撃つ」で緩く、拡散＋原点差で友軍に当たっていた
            //   （FF漏れ実測）。ここで実射ラインを直接検証して塞ぐ。
            if (Cfg.FriendlyFireGate &&
                FriendlyInLineOfFire(self, aimPoint, out int ffBlockerId))
            {
                ReleaseFireIfPressed(self); // [bow] ドロー中ならキャンセル（暴発回避）
                if (Time.time >= _nextHoldLogTime)
                {
                    _nextHoldLogTime = Time.time + Cfg.LogThrottleSec;
                    Log.Out($"[CompanionAI] hold: {threat.Kind} id={tgt.entityId} d={d:0.0}m reason=FF id={ffBlockerId}");
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

            // ★ (3') [bow] 弓/クロスボウ(ItemActionCatapult)は press→ドロー保持→release の3相駆動へ分岐。
            //   銃のフル/セミ分岐は press→次frame release（≒1frame）で strain≈0（矢が足元に落ちる）になるため、
            //   専用ステップに切り出す。前段（射程/shootable/FF/狙点追従/カメラスナップ/ADS）はすべて共有。
            ItemActionCatapult bow = GetHeldCatapult(self, out var bowData);
            if (bow != null && bowData != null)
            {
                if (!Cfg.BowChargeEnabled)
                {
                    // ドロー無効時は弓を撃たない（ドローなしでは strain≈0 で実用にならない）。ドロー中なら安全に引き戻す。
                    if (_bowDrawing) { bow.CancelAction(bowData); _bowDrawing = false; }
                    return;
                }
                BowFireStep(self, in threat, d, bow, bowData, aimMode, bodyPart);
                return;
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

        // ★ [bow] 弓ドロー駆動（3相）。ゲーム側 m_bActivated / m_ActivateTime を信頼の源にする。
        //   Idle → press で ExecuteAction(false)（ItemActionCatapult:123-136: m_bActivated=true, m_ActivateTime=now）
        //   Drawing → (now - m_ActivateTime) が m_MaxStrainTime×BowDrawFraction に達したら release
        //            → ExecuteAction(true)（Catapult:138-148: strain=(経過)/maxStrain を乗せ base で1本発射）
        //   Recover → 再ドロー抑制。厳密な連射律速はゲーム Delay(Catapult:109)に委譲。
        //   ※ strain は Catapult:140 で Clamp01 されない（>1 になり得る）。BowDrawFraction を 1.0 未満に
        //     保つことでフルドロー手前で離し、オーバーチャージ挙動を踏まない。
        private static void BowFireStep(EntityPlayerLocal self, in ThreatInfo threat, float d,
                                        ItemActionCatapult bow, ItemActionCatapult.ItemActionDataCatapult data,
                                        string aimMode, string bodyPart)
        {
            // ゲーム側キャンセル（矢切れ/武器切替/TPカメラNG: Catapult:141-145 等）で活性が落ちたら状態同期
            if (_bowDrawing && !data.m_bActivated)
            {
                _bowDrawing = false;
            }

            if (!_bowDrawing)
            {
                // 発射直後の再ドロー抑制（無駄 press とログの間引き。連射律速はゲーム Delay が担保）
                if (Time.time < _bowNextTry) return;

                int metaBefore = GetHoldingMeta(self);
                self.Attack(false); // press → ExecuteAction(false)

                if (data.m_bActivated)
                {
                    _bowDrawing = true; // ゲーム側が活性化＝ドロー開始成功
                    if (Time.time >= _nextBowLogTime)
                    {
                        _nextBowLogTime = Time.time + Cfg.LogThrottleSec;
                        Log.Out($"[CompanionAI] bow: draw-start {threat.Kind} id={threat.Target.entityId} d={d:0.0}m " +
                                $"mag={metaBefore} maxStrain={data.m_MaxStrainTime:0.00}s frac={Cfg.BowDrawFraction:0.00}");
                    }
                }
                else
                {
                    // 活性化せず＝矢切れでリロード要求(Catapult:113-120) or Delay 中。少し待って再試行。
                    _bowNextTry = Time.time + Cfg.LogThrottleSec;
                    if (Time.time >= _nextBowLogTime)
                    {
                        _nextBowLogTime = Time.time + Cfg.LogThrottleSec;
                        Log.Out($"[CompanionAI] bow: hold (no draw) mag={GetHoldingMeta(self)} — reload/delay.");
                    }
                }
                return;
            }

            // Drawing 中：ゲーム側 m_ActivateTime を基準に経過を測る（Time.time 差でゲームと完全同期）。
            float maxStrain = (data.m_MaxStrainTime > 0.01f) ? data.m_MaxStrainTime : 2.0f;
            float need      = maxStrain * Mathf.Clamp01(Cfg.BowDrawFraction);
            float elapsed   = Time.time - data.m_ActivateTime;
            if (elapsed < need) return; // まだ引き絞り中（狙点追従は前段で継続）

            int before = GetHoldingMeta(self);
            self.Attack(true); // release → 発射
            _bowDrawing = false;

            // 再ドロー抑制。Delay(RPM由来, ItemActionDataRanged.Delay)を尊重し、取れなければ FireInterval で代用。
            float delay = data.Delay;
            _bowNextTry = Time.time + ((delay > 0.01f) ? delay : Cfg.RangedFireIntervalSec);

            int after = GetHoldingMeta(self);
            float strain = (maxStrain > 0.01f) ? Mathf.Clamp01(elapsed / maxStrain) : 1f;
            string hit = "none";
            if (after < before) // 実発砲（Meta減）を検出して命中突合
            {
                Entity hitE = self.MinEventContext != null ? self.MinEventContext.Other : null;
                hit = (hitE == null) ? "none"
                    : (hitE.entityId == threat.Target.entityId ? "TARGET" : "OTHER id=" + hitE.entityId);
            }
            Log.Out($"[CompanionAI] bow: loose {threat.Kind} id={threat.Target.entityId} d={d:0.0}m " +
                    $"strain={strain:0.00} mag={after} aim={aimMode}({bodyPart}) ads={(self.AimingGun ? "on" : "off")} -> hit={hit}");
        }

        // ★ [bow] 保持中アイテムが弓/クロスボウ(ItemActionCatapult)なら action と data を返す。違えば null。
        //   継承: ItemActionCatapult : ItemActionLauncher : ItemActionRanged（Launcher.cs:7 で確認）。
        //   ItemActionDataCatapult は publicize 済みで外部参照可（m_bActivated/m_ActivateTime/m_MaxStrainTime は public フィールド）。
        private static ItemActionCatapult GetHeldCatapult(EntityPlayerLocal self,
                                                          out ItemActionCatapult.ItemActionDataCatapult data)
        {
            data = null;
            var inv = self.inventory;
            var hi  = inv != null ? inv.holdingItem : null;
            var hid = inv != null ? inv.holdingItemData : null;
            if (hi == null || hi.Actions == null || hi.Actions.Length == 0) return null;
            var cat = hi.Actions[0] as ItemActionCatapult;
            if (cat == null) return null;
            if (hid == null || hid.actionData == null || hid.actionData.Count == 0) return null;
            data = hid.actionData[0] as ItemActionCatapult.ItemActionDataCatapult;
            return (data != null) ? cat : null;
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

        // ★ v0.8(D): 友軍射線ガード。
        //   実射 fireShot は GetLookRay().origin(=目, EntityAlive:5536)から狙点方向へ拡散付きで飛ぶ。
        //   ここでは同一原点→aimPoint の直線に対し、対象より手前(dist<狙点距離)で友軍のAABB(膨張)に
        //   交差するものが1体でもあればホールドする。友軍=自分以外の生存プレイヤー＋allyドローン。
        //   膨張量 FriendlyFireMargin は拡散＋コライダー幅ぶんの余裕（片側マージン）。
        private static readonly List<EntityAlive> _ffFriendlies = new List<EntityAlive>();

        private static bool FriendlyInLineOfFire(EntityPlayerLocal self, Vector3 aimPoint, out int blockerId)
        {
            blockerId = -1;
            World world = self.world;
            if (world == null) return false;

            Vector3 origin = self.GetLookRay().origin;          // 実射と同一原点
            Vector3 dir    = aimPoint - origin;
            float   dlen   = dir.magnitude;                     // 対象狙点までの距離（この手前だけ問題）
            if (dlen < 1e-4f) return false;
            Ray shotRay = new Ray(origin, dir / dlen);
            float margin = Cfg.FriendlyFireMargin;

            // --- 友軍集合を集める ---
            _ffFriendlies.Clear();
            var players = world.GetPlayers();                   // リモートのリーダーも含む（FindNearestLeader と同経路）
            if (players != null)
                for (int i = 0; i < players.Count; i++)
                {
                    EntityPlayer p = players[i];
                    if (p != null && p != self && !p.IsDead()) _ffFriendlies.Add(p);
                }
            EntityPlayer selfP = self as EntityPlayer;
            var ents = world.Entities != null ? world.Entities.list : null;
            if (ents != null)
                for (int i = 0; i < ents.Count; i++)
                {
                    // allyドローンのみ友軍に含める（fireShot:1449 と同じ isAlly 判定）
                    if (ents[i] is EntityDrone drone && !drone.IsDead() && drone.isAlly(selfP))
                        _ffFriendlies.Add(drone);
                }

            // --- 射線帯の交差判定 ---
            for (int i = 0; i < _ffFriendlies.Count; i++)
            {
                Bounds b = _ffFriendlies[i].boundingBox;        // world AABB (Entity.boundingBox)
                b.Expand(margin * 2f);                          // Expand は総量増加＝片側 margin
                if (b.IntersectRay(shotRay, out float dist) && dist > 0f && dist < dlen)
                {
                    blockerId = _ffFriendlies[i].entityId;
                    return true;
                }
            }
            return false;
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

        // 保持アイテムの実効リーチ。EngageRange.Read が Dynamic melee(=ItemActionDynamic.Range/RangeDefault)を
        // 正しく解決する。旧実装は基底 ItemAction.Range を読んでいたため、Dynamic melee のリーチを取れず
        // 2.4m 武器を 2.0m フォールバック扱いしていた（実ログで range=2.4 と確認済み）。取れなければ 2.0m。
        private static float GetAttackReach(EntityPlayerLocal self)
        {
            EngageRange.Info er = EngageRange.Read(self);
            if (er.valid && er.range > 0.01f) return er.range;
            return 2.0f;
        }

        // 近接か遠距離か。ItemActionRanged のみ遠距離扱い、それ以外(素手/工具/近接武器)は近接。
        //   ※ [bow] 弓は ItemActionCatapult : ItemActionLauncher : ItemActionRanged なので、ここでも遠距離判定される。
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
