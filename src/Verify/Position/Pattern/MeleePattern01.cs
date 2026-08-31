/*
 *
 * MeleePattern01.cs
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

using CompanionAIVerify.Config;
using CompanionAIVerify.Position.Scene;
using UnityEngine;

namespace CompanionAIVerify.Position.Pattern;

// --- v0.8(B): 格闘オートアプローチ（follow より優先） ---
//   格闘武器 かつ 交戦中脅威が「リーチ外 かつ approachMax 内」のとき、
//   移動目標をリーダーから脅威へ差し替えて前進する（既存の Stop@standoff / Steer→_leader を上書き）。
//   接近steer の後に交戦オーバーレイを回して、リーチに入った瞬間から歩きながら振れるようにする。
// v0.8(B): 格闘オートアプローチの判定＋実行。
//   条件: MeleeAutoApproach ON / 交戦中脅威あり / 格闘武器保持 / reach < d <= approachMax。
//   距離 d は ActionDriver の swing ゲートと同じ threat.DistSq(feet-to-feet)基準に揃える。
//   reach は EngageRange の実効リーチ（Dynamic melee も正しく解決）。
//   停止は d<=reach。swing は ActionDriver 側で reach+ReachBuffer から開くので、
//   接近の最終区間は「歩きながら振る」→ reach で停止して振り続ける、と滑らかに繋がる。
/*
 * 脅威が近距離に存在する & 格闘攻撃で応戦する場合のアクション実装
 * 格闘攻撃のリーチ内まで脅威の方向へ歩いて接近を行う
 * 移植元メソッド : TryMeleeApproach
 */
internal class MeleePattern01 : IPositionPattern
{
    private InfoHolder _i;

    string IPositionPattern.Name => "MeleePattern01";

    void IPositionPattern.Run(InfoHolder i)
    {
        _i = i;

        var er = EngageRange.Read(_i.Self);
        if (!er.valid || er.isRanged) return;

        var reach = er.range > 0.01f ? er.range : 2.0f;
        var d = Mathf.Sqrt(_i.Threat.DistSq);

        // 停止距離はリーチより StepIn ぶん内側に置く
        // リーチ端 ( d ≈ reach ) に張り付くと d_eyeChest > Range になりがちで空振りが混ざり、ターゲットの揺れで「振れるが届かない帯 ( reach ~ reach + ReachBuffer ) 」に戻ってしまう
        // 少し踏み込ませて d_eyeChest < Range の内側で安定させ、inRange を True に保つ ( スイングの間欠発火＝スローペースも解消 )
        // 下限クランプ : StepIn を過大設定してもゾンビへ突っ込まないよう最低 0.8 m は空ける
        var stopDist = Mathf.Max(0.8f, reach - Cfg.MeleeApproachStepIn);

        // 停止距離内なら接近不要 ( その場で振る )
        // approachMax 外なら追わない ( リーダーから離れ過ぎ防止 )
        if (d <= stopDist || d > Cfg.MeleeApproachMaxDistance) return;

        var lookDir = _i.Threat.Target.position - _i.Self.position;
        lookDir.y = 0.0f;
        _i.SteerOperator.Run(_i.Threat.Target.position, lookDir, false); // 数m の詰めは歩き（照準安定）
    }
}