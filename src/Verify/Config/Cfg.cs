/*
 *
 * Cfg.cs
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

namespace CompanionAIVerify.Config;

// --- Tunables (companion_config.txt で上書き可能) --------------------------
internal static class Cfg
{
    internal const KeyCode ToggleKey = KeyCode.F8;
    internal static string ModVersion = "0.8.1";

    internal static bool Enabled = false; // 起動時OFF。F8でトグル(ファイル対象外)
    internal static float StandoffMeters = 3.0f; // これ以内なら停止
    internal static float RunMeters = 8.0f; // これ以上離れたら走る

    // --- 経路追従（navigation スライス3） ---
    internal static bool PathFollow = true; // 受信経路があればWP追従、無ければ直線
    internal static float WaypointArriveM = 1.0f; // WP到達半径(水平)
    internal static float WaypointHeightTolM = 1.5f; // WP到達の高さ許容
    internal static float PathStaleSec = 3.0f; // これより古い経路は無視し直線へ戻す

    // --- 障害物ジャンプ（ver0.8.1） ---
    //   前進中に onGround(接地)＆isCollidedHorizontally(前方に詰まった)を検出し、前方セルが
    //   「脛の高さにブロック／頭の高さは空」＝1ブロック段差なら movementInput.jump で乗り越える。
    //   2ブロック以上の壁は頭が塞がるためジャンプしない（無駄ジャンプ抑止）。
    internal static bool JumpObstacles = true; // 段差ジャンプ ON/OFF
    internal static float JumpProbeAhead = 0.6f; // 前方プローブ距離(m)。段差セルを見に行く距離。大きいほど早出しジャンプ

    internal static float ThreatScanRadius = 20.0f; // 脅威走査半径(m)
    internal static bool CombatMode = true; // true=脅威を向く/叩く
    internal static float LogThrottleSec = 0.5f; // 検知/交戦ログの最小間隔

    // --- 交戦スライス（近接, ver0.3） ---
    internal static float ReachBuffer = 0.5f; // 近接射程判定の余裕(m)

    // ★ 実ログで bFirstPersonView の実値を観測。false と分かれば true で spawn 誤設定を自己修復。
    internal static bool ForceFirstPerson = false;

    // --- 発砲スライス（遠距離, ver0.4） ---
    internal static bool EnableRangedFire = true; // false で従来の deferred ログのみ
    internal static float RangedMaxEngageMeters = 18.0f; // これ以内の脅威にのみ発砲(m)。グローバル上限（挙動キャップ）
    internal static float RangedRangeSafety = 0.85f; // v0.8(C): 武器の実効射程(GetRange)にこの係数を掛けた距離までしか撃たない。弾が届かない距離での空撃ちを防ぐ
    internal static bool FriendlyFireGate = true; // v0.8(D): 射線帯に友軍(他プレイヤー＋allyドローン)が居れば発砲しない
    internal static float FriendlyFireMargin = 0.4f; // v0.8(D): 友軍AABBの片側膨張(m)。拡散＋コライダー幅ぶんの余裕。大きいほど安全側(撃たない)
    internal static float RangedFireIntervalSec = 0.4f; // 発砲ケイデンス(≒2.5発/秒)

    // --- 弓/クロスボウ引き絞り（ItemActionCatapult, ver0.8.1） ---
    //   弓は「press でチャージ開始→一定時間ドロー保持→release で発射」。銃の press→次frame release では
    //   strain≈0（矢が足元に落ちる）になるため、ItemActionCatapult 専用の3相駆動で引き絞ってから離す。
    //   ドロー時間は武器の m_MaxStrainTime（XML "Max_strain_time" 既定2s を RPM 調整した実値, Catapult:59-67）を
    //   実行時に読み、その割合(BowDrawFraction)まで引いてから release する。
    internal static bool BowChargeEnabled = true; // false で弓を撃たない（ドローなしでは実用にならないためホールド）

    internal static float
        BowDrawFraction =
            0.95f; // フルドロー(m_MaxStrainTime)の何割まで引くか。strain は Clamp01 されない(Catapult:140)ため 1.0 未満でオーバーチャージ回避

    // --- ハイブリッド狙点（ver0.5） ---
    //   実測: 命中は headLift≈1.5-2.0、非命中は≈0-0.7 で分離。
    internal static float HeadAimMinLift = 1.2f; // これ以上なら頭狙い、未満は胴中心

    // --- カメラ配達ラグ対策（ver0.6）＋視差補正（ver0.7） ---
    //   発砲直前に playerCamera.transform を狙点へ即時スナップし、配達ラグ＋視差を解消。
    internal static bool SnapCameraOnFire = true;

    // --- 視差A/B用トグル（v0.5.3） ---
    //   true=カメラ実位置基準（補正あり） / false=頭ボーン基準（補正なし・旧挙動）。
    internal static bool AimFromCameraOrigin = true;

    // --- ADS（サイトを覗く射撃, ver0.8） ---
    //   AimingGun=true で拡散が hip(1.0)→aiming(0.1) と10倍縮む(ItemActionRanged:1346, 748)。
    internal static bool AimDownSightsOnEngage = true;

    // --- hit検証＋シュータブル・ゲート（v0.6.0） ---
    //   発砲前に自前 Voxel.Raycast で「射線が対象コライダーに届くか」を検証。
    //   候補狙点(頭/胴中心/腹)を順に試し、対象に当たる点だけ採用。全滅なら撃たずホールド。
    //   遮蔽(block)・FF(別entity)・空(sky) を理由としてログ化。
    internal static bool RequireShootable = true;

    // --- フルオート連射（v0.6.0） ---
    //   GetBurstCount==0 の武器はトリガー押しっぱなしで RPM 連射（false で全銃 FireInterval 単発）。
    internal static bool FullAutoHold = true;

    // --- (A) 武器自動切替（v0.7） ---
    internal static bool AutoWeaponSwitch = true;
    internal static float SwitchToMeleeMeters = 3.5f; // これ以下→近接
    internal static float SwitchToRangedMeters = 5.5f; // これ以上→銃（間はデッドバンド）
    internal static float WeaponSwitchMinIntervalSec = 0.6f; // 連続切替の最小間隔
    internal static float LoadoutScanIntervalSec = 1.0f; // ツールベルト走査throttle

    // --- (B) ツールベルト優先配置（v0.7） ---
    internal static bool AutoStowWeaponsToToolbelt = true;
    internal static float ToolbeltStowIntervalSec = 5.0f;
    internal static bool StowDynamicMelee = true; // dynamic melee も武器扱い（工具含む点に注意）

    // --- (C) リーダー落下物拾得（v0.7） ---
    internal static bool AutoPickupLeaderDrops = true;
    internal static float PickupRadius = 6.0f;
    internal static float PickupScanIntervalSec = 0.5f;
    internal static bool PickupUnowned = false; // belongsPlayerId<=0 も拾う

    // ★ v0.7.1: 武器認識（stow / select 共用）
    internal static string WeaponClassifyMode = "auto"; // "auto" | "strict"
    internal static string MeleeWeaponNames = ""; // include（XML名, CSV）
    internal static string RangedWeaponNames = ""; // include（XML名, CSV）
    internal static string WeaponExcludeNames = "meleeToolTorch"; // 除外（XML名, CSV）※名前は要XML確認
    internal static string MeleeWeaponTags = ""; // include（タグ, CSV）
    internal static string RangedWeaponTags = ""; // include（タグ, CSV）
    internal static string WeaponExcludeTags = ""; // 除外（タグ, CSV）

    // v0.8.0: 交戦マニューバ
    internal static bool LogEngageRange = true; // Slice A 観察用。B に入るとき false でOK
    internal static float EngageLogMinInterval = 0.5f; // ログ時間ゲート(秒)

    // v0.8.0(B): 格闘オートアプローチ ＋ 照準補正(A)
    //   ・アプローチ: 格闘武器 かつ リーチ外の交戦中脅威が approachMax 内なら、移動目標をリーダー→脅威へ差し替え前進。
    //     停止距離は EngageRange の実効リーチ（Dynamic melee も正しく解決）＝ d<=reach で停止。swing は reach+ReachBuffer から開く。
    //   ・照準補正: 交戦中 attackTarget を直接代入し、近接レイをチェストへ自動補正（ItemActionDynamic:327-330）。FaceTarget3D 精度に非依存化。client-safe（SetAttackTarget は entityDistributer null で NRE のため不使用）。
    internal static bool MeleeAutoApproach = true; // 格闘オートアプローチ ON/OFF
    internal static float MeleeApproachMaxDistance = 6.0f; // コンパニオン中心、この距離以内の脅威のみ接近対象(m)。小さめ推奨（リーダーから離れ過ぎ防止）

    internal static float
        MeleeApproachStepIn = 0.7f; // v0.8(D後): 接近の停止距離をリーチより内側へ(reach-StepIn)。リーチ端張り付きの空振りを防ぐ。大きいほど踏み込む

    internal static bool MeleeAimAssist = true; // (A) attackTarget 直接代入による近接チェスト自動補正（client-safe）

    internal static int
        MeleeAimAssistHoldTicks =
            30; // ※現状未使用: client では attackTargetTime を使えない（失効パスが entityDistributer.SendPacket を踏む）。将来server側実装用に予約

    // v0.8.0(B) テスト用ハーネス: ゾンビの attackTarget をリーダーへ固定（単独交戦で「一定の間合い」を観察するため）。
    //   ホスト側で全敵対の標的をリーダーへ書き換える（Patch_PinTargetToLeader）。通常運用では false。
    internal static bool DebugPinTargetToLeader = false;

    // v0.8.0(B) テスト用ハーネス: 敵対をその場に固定（交戦状態のまま一歩も動かさない）。
    //   MeleeApproachMaxDistance 検証で「N m に静止した交戦中ゾンビ」を再現する（Patch_FreezeHostiles）。通常運用では false。
    internal static bool DebugFreezeHostiles = false;
}