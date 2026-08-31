/*
 *
 * AimOperator.cs
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

using CompanionAIVerify.Combat.Scene;
using CompanionAIVerify.Config;
using UnityEngine;

namespace CompanionAIVerify.Combat.Operation;

internal class AimOperator
{
    private readonly InfoHolder _i;

    internal AimOperator(InfoHolder i)
    {
        _i = i;
    }

    // v0.8(B)-A: SetAttackTarget を張ったか（解除用）
    internal bool AimAssistSet { get; set; }

    internal Vector3 CamWorld { get; private set; }

    internal Vector3 AimPoint { get; private set; }

    internal string Mode { get; private set; }

    internal string Part { get; private set; }

    internal string Reason { get; private set; }

    // シュータブル候補狙点の再利用バッファ ( 毎フレーム alloc 回避 ) : 照準
    internal Vector3[] CandPts { get; private set; }

    // シュータブル候補狙点の再利用バッファ（毎フレーム alloc 回避） : 照準の名前
    internal string[] CandNm { get; private set; }

    internal bool Shootable { get; private set; }

    // ★ v0.8(B)-A
    // 近接レイをターゲットのチェストへ自動補正させる
    //   ItemActionDynamic.GetExecuteActionTarget は attackTarget != null のとき ray を getChestPosition() 方向へ差し替える ( ItemActionDynamic : 327-330 )
    //   これで FaceTarget3D の平面精度に依存せず命中が安定する
    //
    //   ※ client では SetAttackTarget() を使えない : 内部で world.entityDistributer.SendPacket を叩くが entityDistributer は IsServer 時のみ生成される ( World : 468-477 ) ため client では null -> NRE
    //     さらに attackTargetTime > 0 にすると自動失効パス ( EntityAlive : 3367-3376 ) も同じ null を踏む
    //     -> public フィールドへ直接代入し、attackTargetTime は 0 のまま ( 失効パスに入らせない )
    //       解除は ReleaseIfPressed で直接 null 代入
    //       redirect は attackTarget を読むだけ ( GetAttackTarget : 5890 ) なので十分
    //       ダメージ同期は Attack() -> DamageEntity() -> NetPackageDamageEntity 経由で別途成立 ( attackTarget 非依存 )
    internal void MeleeAim()
    {
        if (Cfg.MeleeAimAssist)
        {
            _i.Self.attackTarget = _i.Target.Target; // EntityAlive : 716 ( public field ) — client-safe
            AimAssistSet = true;
        }
    }

    internal void RangeAim()
    {
        if (Cfg.RequireShootable)
            RangeAimResolve();
        else
            RaingeAimSimple();
    }

    // ★ ( 1 ) 射線が対象に届く狙点を探す
    private void RangeAimResolve()
    {
        var world = _i.Self.world;
        var headLift = _i.Target.Target.getHeadPosition().y - _i.Target.Target.position.y;
        var head = _i.Target.Target.getHeadPosition();
        var center = _i.Target.Target.position + Vector3.up * _i.Target.Target.scaledExtent.y;
        var belly = _i.Target.Target.getBellyPosition();
        var haveReason = false;
        var ml = _i.Self.GetModelLayer();

        // カメラ実ワールド位置 ( = 弾の原点 )
        // 視差補正の基準
        CamWorld = Cfg.AimFromCameraOrigin && _i.Self.playerCamera != null
            ? _i.Self.playerCamera.transform.position + Origin.position
            : _i.Self.getHeadPosition();
        AimPoint = _i.Target.Target.position;
        Mode = "none";
        Part = "-";
        Reason = "sky";
        CandPts = new Vector3[3];
        CandNm = new string[3];
        Shootable = false;
        if (headLift >= Cfg.HeadAimMinLift)
        {
            CandPts[0] = head;
            CandNm[0] = "head";
            CandPts[1] = center;
            CandNm[1] = "center";
        }
        else
        {
            CandPts[0] = center;
            CandNm[0] = "center";
            CandPts[1] = head;
            CandNm[1] = "head";
        }

        CandPts[2] = belly;
        CandNm[2] = "belly";
        _i.Self.SetModelLayer(2); // 自己を射線から除外 ( fireShot と同じ )
        try
        {
            for (var i = 0; i < 3; i++)
            {
                var dir = CandPts[i] - CamWorld;
                if (dir.sqrMagnitude < 1e-6f) continue;
                var range = dir.magnitude + 1.0f;
                if (!Voxel.Raycast(world, new Ray(CamWorld, dir.normalized), range, -538751005, 8, 0f))
                {
                    if (!haveReason)
                    {
                        haveReason = true;
                        Reason = "sky";
                    }

                    continue;
                }

                var info = Voxel.voxelRayHitInfo.Clone();
                var e = ItemActionAttack.FindHitEntityNoTagCheck(info, out var bp);
                if (e != null && e.entityId == _i.Target.Target.entityId)
                {
                    AimPoint = CandPts[i];
                    Mode = CandNm[i];
                    Part = string.IsNullOrEmpty(bp) ? "body" : bp;
                    Reason = "ok";
                    Shootable = true;
                    return;
                }

                if (!haveReason)
                {
                    haveReason = true;
                    if (e != null) Reason = "OTHER id=" + e.entityId;
                    else
                        Reason = "block:" + (string.IsNullOrEmpty(info.tag)
                            ? info.transform != null ? info.transform.name : "?"
                            : info.tag);
                }
            }
        }
        finally
        {
            _i.Self.SetModelLayer(ml);
        }

        Shootable = false;
    }

    // ゲート無効時は従来のハイブリッド狙点をそのまま採用 ( 検証なし )
    private void RaingeAimSimple()
    {
        var headLift = _i.Target.Target.getHeadPosition().y - _i.Target.Target.position.y;
        var useHead = headLift >= Cfg.HeadAimMinLift;
        AimPoint = useHead
            ? _i.Target.Target.getHeadPosition()
            : _i.Target.Target.position + Vector3.up * _i.Target.Target.scaledExtent.y;
        Mode = useHead ? "head" : "center";
        Part = "-";
        Reason = "ok";
        Shootable = true;
    }

    // このメソッドは RangeAim 呼び出し後に実行すること
    // body / 視覚トラッキング ( 見た目の照準 ) はホールド中も維持
    //   意図方向 ( aimDir ) で body / camera を向ける
    //   ranged ショットは GetLookRay ( camera ) 由来なので SetRotation がカメラ Angle を更新する ( ItemActionRanged : 1579, EPL : 2310 )
    //   ただしカメラ transform 反映は遅延するため、ラグ対策は呼び出し側で別途スナップする
    internal void RangeRotate()
    {
        var aimDir = AimPoint - _i.Self.getHeadPosition();
        if (aimDir.sqrMagnitude < 1e-6f) return;
        var euler = Quaternion.LookRotation(aimDir.normalized, Vector3.up).eulerAngles;
        euler.x *= -1f; // ピッチ反転 ( EPL : 239, 251 )
        _i.Self.SetRotation(euler);
    }

    // このメソッドは RangeAim 呼び出し後に実行すること
    // ★ ( 2 ) 発砲準備 : ADS + カメラを狙点へスナップ
    internal void RangeAimSnapCamera()
    {
        var shotDir = AimPoint - CamWorld;
        _i.AdsOperator.Run(true);
        if (Cfg.SnapCameraOnFire && _i.Self.playerCamera != null && shotDir.sqrMagnitude > 1e-6f)
            _i.Self.playerCamera.transform.rotation = Quaternion.LookRotation(shotDir.normalized, Vector3.up);
    }

    // ItemActionLauncher ( クロスボウ・ロケットランチャー ), LauncherPatternXX 専用
    // 内容が RangeAimSnapCamera とほぼ同じ, RangeAimSnapCamera への副作用を防止するため分けて実装している
    // RangeAimSnapCamera へ渡すフラグにより分岐を増やすか考え中
    internal void RangeAimSnapCameraLauncher()
    {
        var shotDir = AimPoint - CamWorld;
        _i.AdsOperator.RunLauncher(true);
        if (Cfg.SnapCameraOnFire && _i.Self.playerCamera != null && shotDir.sqrMagnitude > 1e-6f)
            _i.Self.playerCamera.transform.rotation = Quaternion.LookRotation(shotDir.normalized, Vector3.up);
    }
}