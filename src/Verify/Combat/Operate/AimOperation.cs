/*
*
* AimOperation.cs
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

namespace CompanionAIVerify.Combat.Operate
{
    internal class AimOperation
    {
        private InfoHolder _i;
        private LogInfoHolder _li;

        private Vector3 _camWorld;
        internal Vector3 CamWorld { get => _camWorld; }

        private Vector3 _aimPoint;
        internal Vector3 AimPoint { get => _aimPoint; }

        private string _mode;
        internal string Mode { get => _mode; }

        private string _part;
        internal string Part { get => _part; }

        private string _reason;
        internal string Reason { get => _reason; }

        private Vector3[] _candPts;
        internal Vector3[] CandPts { get => _candPts; }

        private string[] _candNm;
        internal string[] CandNm { get => _candNm; }

        private bool _shootable;
        internal bool Shootable { get => _shootable; }

        internal AimOperation(InfoHolder i, LogInfoHolder li)
        {
            _i = i;
            _li = li;
        }

        internal void MeleeAim()
        {
            if (Cfg.MeleeAimAssist)
            {
                _i.Self.attackTarget = _i.Target.Target; // EntityAlive : 716 ( public field ) — client-safe
                _i.AimAssistSet = true;
            }
        }

        internal void RangeAim()
        {
            if (Cfg.RequireShootable)
            {
                RangeAimResolve();
            }
            else
            {
                RaingeAimSimple();
            }
        }

        // ★ ( 1 ) 射線が対象に届く狙点を探す
        private void RangeAimResolve()
        {
            float     headLift   = _i.Target.Target.getHeadPosition().y - _i.Target.Target.position.y;
            Vector3   head       = _i.Target.Target.getHeadPosition();
            Vector3   center     = _i.Target.Target.position + Vector3.up * _i.Target.Target.scaledExtent.y;
            Vector3   belly      = _i.Target.Target.getBellyPosition();
            bool      haveReason = false;
            int       ml         = _i.Self.GetModelLayer();
            // カメラ実ワールド位置 ( = 弾の原点 )
            // 視差補正の基準
            _camWorld  = (Cfg.AimFromCameraOrigin && _i.Self.playerCamera != null)
                ? _i.Self.playerCamera.transform.position + Origin.position
                : _i.Self.getHeadPosition();
            _aimPoint  = _i.Target.Target.position;
            _mode      = "none";
            _part      = "-";
            _reason    = "sky";
            _candPts   = new Vector3[3];
            _candNm    = new string[3];
            _shootable = false;
            if (headLift >= Cfg.HeadAimMinLift) { _candPts[0] = head;   _candNm[0] = "head";   _candPts[1] = center; _candNm[1] = "center"; }
            else                                { _candPts[0] = center; _candNm[0] = "center"; _candPts[1] = head;   _candNm[1] = "head";   }
            _candPts[2] = belly; _candNm[2] = "belly";
            _i.Self.SetModelLayer(2); // 自己を射線から除外 ( fireShot と同じ )
            try
            {
                for (int i = 0; i < 3; i++)
                {
                    Vector3 dir = candPts[i] - camWorld;
                    if (dir.sqrMagnitude < 1e-6f) continue;
                    float range = dir.magnitude + 1.0f;
                    if (!Voxel.Raycast(world, new Ray(camWorld, dir.normalized), range, -538751005, 8, 0f))
                    {
                        if (!haveReason) { haveReason = true; reason = "sky"; }
                        continue;
                    }
                    WorldRayHitInfo info = Voxel.voxelRayHitInfo.Clone();
                    Entity e = ItemActionAttack.FindHitEntityNoTagCheck(info, out string bp);
                    if (e != null && e.entityId == _i.Target.Target.entityId)
                    {
                        aimPoint = candPts[i]; mode = candNm[i]; part = string.IsNullOrEmpty(bp) ? "body" : bp; reason = "ok";
                        _shootable = true;
                        return;
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
            finally { _i.Self.SetModelLayer(ml); }
            _shootable = false;
        }

        // ゲート無効時は従来のハイブリッド狙点をそのまま採用 ( 検証なし )
        private void RaingeAimSimple()
        {
            float headLift = _i.Target.Target.getHeadPosition().y - _i.Target.Target.position.y;
            bool useHead = headLift >= Cfg.HeadAimMinLift;
            _aimPoint = useHead ? _i.Target.Target.getHeadPosition() : _i.Target.Target.position + Vector3.up * _i.Target.Target.scaledExtent.y;
            _mode = useHead ? "head" : "center";
            _part = "-";
            _reason = "ok";
            _shootable = true;
        }

        // このメソッドは RangeAim 呼び出し後に実行すること
        // body / 視覚トラッキング ( 見た目の照準 ) はホールド中も維持
        //   意図方向 ( aimDir ) で body / camera を向ける
        //   ranged ショットは GetLookRay ( camera ) 由来なので SetRotation がカメラ Angle を更新する ( ItemActionRanged : 1579, EPL : 2310 )
        //   ただしカメラ transform 反映は遅延するため、ラグ対策は呼び出し側で別途スナップする
        internal void RangeRotate()
        {
            Vector3 aimDir = _aimPoint - _i.Self.getHeadPosition();
            if (aimDir.sqrMagnitude < 1e-6f) return;
            Vector3 euler = Quaternion.LookRotation(aimDir.normalized, Vector3.up).eulerAngles;
            euler.x *= -1f; // ピッチ反転 ( EPL : 239, 251 )
            _i.Self.SetRotation(euler);
        }

        // このメソッドは RangeAim 呼び出し後に実行すること
        // ★ ( 2 ) 発砲準備 : ADS + カメラを狙点へスナップ
        internal void RangeAimSnapCamera()
        {
            Vector3 shotDir = _aimPoint - _camWorld;
            _i.ADSOperation.Run(true);
            if (Cfg.SnapCameraOnFire && _i.Self.playerCamera != null && shotDir.sqrMagnitude > 1e-6f)
            {
                _i.Self.playerCamera.transform.rotation = Quaternion.LookRotation(shotDir.normalized, Vector3.up);
            }
        }
    }
}
