/*
*
* CompanionAIVerify.cs
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

using HarmonyLib;

using CompanionAIVerify.Config;

// =============================================================================
// Companion AI verify harness — Build v0.7.1
//   変更点: 武器認識を config 可能化。単一の WeaponClassifier を新設し、
//           WeaponSelector（自動選択）と ItemStower（ツールベルト昇格）が共用する。
//           - auto  : 型フォールバック有効（工具も近接武器扱い）＋名前/タグで除外・追加
//           - strict: include に載せた名前/タグのみ武器（全指定）
//
// Companion AI verify harness — Build v0.7.0
//   (A) 交戦距離に応じた武器自動切替（近接⇄銃）  … 新 engage capability
//   (B) 武器のツールベルト優先配置                … 非ホットパス・ユーティリティ
//   (C) リーダー落下物の自動拾得                  … 非ホットパス・ユーティリティ（検証込み）
//
// Companion AI verify harness — Build v0.6.1 (hit検証+シュータブル・ゲート / フルオート連射+空リロード修正)
//   採番方針: 交戦(engage)スライス系列は v0.5.x。tuning/診断/tooling は patch(.1,.2,.3,.4)、
//   新capability(engage-maneuver/navigation 等)が入る時のみ minor を上げる。
//   v0.6.0: 発砲前に Voxel.Raycast で射線検証（遮蔽/FF/狙点外しを撃たずホールド, 候補狙点探索）
//          ＋ フルオート武器(GetBurstCount==0)を press 保持で RPM 連射。
// -----------------------------------------------------------------------------
// Companion AI — locomotion + facing + threat-sensing + ENGAGE(melee+ranged)
// (7DTD 3.1.0)
// -----------------------------------------------------------------------------
// このスライスで追加したもの: 発砲スライス（Section F）。ver0.3(近接)に ranged を追加。
//   (1) 交戦の手前で bFirstPersonView / TPCameraCheck を実ログ出力して確定（ver0.3から）。
//   (2) 近接: アクティブ最近傍脅威が射程内なら press 駆動スイング（ver0.3から）。
//   (3) 遠距離: 銃保持時、頭部狙点へエイム→press/release サイクルで発砲。
//       弾切れはゲート自動リロード任せ、実発砲は Meta 差で検出しログ。
//
// ★ ranged 発火モデルの決定的な差（ver0.3 melee と異なる）:
//   セミオート(BurstRoundCount==1)は bInitialPress（押下の立ち上がりフレーム）でしか
//   発火しない(ItemActionRanged:1121,1160)。press を張り続けても初弾のみ。
//   → 連射には press↔release サイクルが必須。melee の press 張りっぱなしは通用しない。
//
// ★ 弾切れ→自動リロード:
//   空撃ち時 CanReload なら requestReload→GameManager.ItemReloadServer(1244-1246,622)。
//   リロード中は Reloading() が press を吸収(1182)、明けたら再開。
//   → トリガーを引き続けるだけでドライバ側のリロード管理は不要。
//
// ★ netsync: 発砲エフェクト/ダメージは ItemActionEffectsServer(1286) でサーバへ複製。
//   ranged も直接 Attack() で netsync-safe（melee と同じ確定事項）。
//
// ★ 実発砲の検出: 弾はチャンバー基準 holdingItemItemValue.Meta(A4)が ConsumeAmmo(1264)
//   で減る。press 前後の Meta 差で「実際に1発出た」を検出→ログ（swallow を誤カウントしない）。
//
// ★ ヘッドショット狙点: getHeadPosition()(Entity:2642=emodel.GetHeadPosition())。
//   両端の頭で dir = target.getHeadPosition() - self.getHeadPosition() が最精度。
//   （近接は SphereRadius 許容があるので従来どおり胴狙い +0.9m のまま）
//
// ── ver0.4.1 追加(計測): fire ログに intended vs actual-hit を記録 ──
//   狙ったターゲット(intended) = threat.Target（我々の選択）。
//   実際に当てたもの(actual)   = self.MinEventContext.Other（fireShot がゲート内で設定,
//     命中エンティティ / 非命中は null。ItemActionRanged:1194 null化, 1462 格納）。
//   → hit=TARGET / OTHER id=N(FF疑い) / none(block/miss) を判別。
//
// ── ver0.5 追加(修正): ハイブリッド狙点 ──
//   計測結果: 命中は headLift≈1.5-2.0、非命中は≈0-0.7（負値は頭が足元より下）。
//   頭ボーンは低姿勢(四足/突進/のけぞり)で当たり判定を外すため、headLift でゲート:
//     headLift >= HeadAimMinLift → 頭狙い（立ち姿勢のヘッドショット維持）
//     未満                        → position + up*scaledExtent.y（AABB縦中心, 姿勢非依存）
//   fire ログに aim=head/center と aimLift を追加し、低headLift の弾が
//   none→TARGET に転じるかをテストで直接集計できるようにする（しきい値は要調整）。
//
// ── ver0.6 追加(診断+修正候補): カメラ配達ラグ ──
//   観察: body(見た目)は標的を向くのに弾が上へ抜ける／シングルなら当たる。
//   原因仮説: 弾は GetLookRay()=playerCamera.transform 由来だが、SetRotation は
//     カメラを m_vp_FPCamera.Angle 経由で遅延反映(vp_FPCamera更新はLateUpdate付近)。
//     同フレーム発砲では前フレームのカメラ向きで撃つ→急ピッチの低標的で上に外す。
//   診断: fire ログに errDeg(実レイ GetLookRay vs 意図方向) と pWant/pAct(ピッチ) を追加。
//   修正候補(トグル SnapCameraOnFire): 発砲直前に playerCamera.transform を狙点へ即時スナップ。
//     false=ベースライン(errDeg 大を確認) / true=errDeg≈0 と命中改善を確認（同一セッションA/B）。
//
// ── ver0.7 追加(修正): 視差(パララックス)補正 ──
//   実測: snap=true で errDeg=0（配達ラグ解消）だが、至近＋大俯角のみ none 連発。
//   原因: 狙い方向を頭ボーン基準で作っていたが、弾は GetLookRay()=カメラ位置から出る。
//     頭とカメラの微小オフセットは遠距離で無視できても至近で致命的（距離依存の外れ）。
//   修正: スナップ方向を「カメラ実ワールド位置(playerCamera.transform.position+Origin.position)
//     → 狙点」で計算。Entity.position はワールド(Entity:938)。既定 SnapCameraOnFire=true。
//   診断: missDist（実レイ GetLookRay と aimPoint の最短距離）を追加。至近で≈0 に落ちれば視差確定。
//
// ── ver0.8 追加(改善): ADS（サイトを覗く射撃） ──
//   これまで全弾ヒップ＝最大拡散。AimingGun=true で拡散 hip(1.0)→aiming(0.1) の10倍縮小
//   (ItemActionRanged:1346, 更新は 748 で holdingEntity.AimingGun を参照)。
//   視差(v0.7)とは別軸で、狙点周りの散布界を絞る。発砲前に SetAds(true)、
//   離脱時 ReleaseFireIfPressed で SetAds(false)。secondary action(Actions[1]) 持ちのみ。
//   fire ログに ads=on/off を追加。
//
// ★ bFirstPersonView が「実行時に決まる」ことの接地（監査より重要）:
//   spawn/respawn 時 AfterPlayerRespawn(EPL:3715) → AttachedToEntity==null なら
//   SwitchToPreferredCameraMode(EPL:3645) が走る。そこで
//     CameraRestrictionMode==0 → SetFirstPersonView(bPreferFirstPerson, ...)
//     bPreferFirstPerson は OptionsGfxDefaultFirstPersonCamera(EPL:1282) 由来
//     CameraRestrictionMode!=0 → SetFirstPersonView(num==1, ...)（サーバ強制）
//   ＝ コンパニオンPCのグラフィック設定 or サーバ設定で false になり得る。
//   → デフォルト true(EPL:395) は保証されない。だから実ログで確定させる。
//
// ★ bFirstPersonView==true で攻撃ゲートが全消しになる接地:
//   CharacterCameraAngleValid(EPL:5969): if(bFirstPersonView||Locked3rdPerson) return Pass;
//   canStartAttack(ItemActionDynamicMelee:337): TPCamera分岐は { bFirstPersonView:false } 限定。
//   さらに eTPCameraCheckResult.Pass==0（enum既定値）で二重に安全。
//
// ★ 攻撃レイは camera 由来 → SetRotation で操舵できる:
//   GetLookRay/GetMeleeRay(EPL:3847,3869) は playerCamera から発射。
//   SetRotation(EPL:2310) は m_vp_FPCamera.Angle を更新 → facing用の SetRotation が
//   そのまま攻撃レイを操舵する（facingスライスで視覚確認済みの経路を再利用）。
//
// ★ 実行モデル（ItemActionDynamicMelee）:
//   Attack(false)=press: canStartAttack 通過で Attacking=true＝スイング開始(EAlive:6164→6142)
//   Attack(true)=release: SetAttackFinished（後始末）
//   実ヒットは hold 中に Inventory が holdingItem.OnHoldingUpdate(Inventory:403) を
//   毎フレーム駆動して適用。press を張り続けると canStartAttack の APM 律速
//   (ItemActionDynamicMelee:358) がケイデンスを自動制御 → 多重発火なし。
//   ダメージのレプリケーションは下流 DamageEntity→SendToServer(NetPackageDamageEntity)
//   に内包＝直接 Attack() 呼び出しは netsync-safe（監査確定事項）。
//
// 本スライスの範囲(意図的に絞る):
//   - 近接のみ。Actions[0] is ItemActionRanged は「遠距離＝撃たない」でログのみ。
//   - 脅威への接近(engage maneuver)は未実装。射程内に来た脅威のみ叩く（据え置き）。
//   - 攻撃対象は「アクティブ最近傍脅威」。友軍(リーダー)狙い脅威の拾い上げは別スライス。
//
// 導入: COMPANION クライアントPCにのみ入れる。F8 で駆動ON/OFF。
// 参照DLL: Assembly-CSharp.dll / UnityEngine.CoreModule.dll / 0Harmony.dll
// =============================================================================

namespace CompanionAIVerify
{
    internal enum ThreatKind { Zombie, EnemyAnimal, HostileHuman, OtherEnemy, PassiveAnimal, Player, Unknown }
    internal enum Awareness  { Unawakened, Awakening, Engaged }

    internal enum WeaponMode { None, Melee, Ranged }

    // --- Mod entry -----------------------------------------------------------
    public class CompanionAIVerifyModApi : IModApi
    {
        public void InitMod(Mod _modInstance)
        {
            var harmony = new Harmony("companionai.verify");
            harmony.PatchAll();
            ModCfgFile.Init(_modInstance);   // companion_config.txt を読込（無ければ生成）
            Log.Out("[CompanionAI] verify harness v0.6.1 loaded (engage[melee+ranged/parallax/ADS/shootable-gate/full-auto] + file-config). F8 to toggle drive / reload config.");
        }
    }

    // --- Harmony patch -------------------------------------------------------
    [HarmonyPatch(typeof(EntityPlayerLocal), "MoveByInput")]
    internal static class Patch_EntityPlayerLocal_MoveByInput
    {
        private static void Prefix(EntityPlayerLocal __instance)
        {
            CompanionExecutor.OnMovePrefix(__instance);
        }
    }
}
