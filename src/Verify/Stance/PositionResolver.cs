/*
 *
 * PositionResolver.cs
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
using CompanionAIVerify.Perception;
using UnityEngine;

namespace CompanionAIVerify.Stance;

/*
 * MOD 操作キャラクターの位置調整を解決する
 */
internal class PositionResolver
{
    internal PositionResolver()
    {
        Pattern = Patterns.None;
    }

    internal Patterns Pattern { get; private set; }

    // どこへ移動するのか ( = どの PositionPattern を使用するか ) 決定
    // 早い判断と n8n 意図の差し込み口になる
    internal void Run(EntityPlayerLocal self, in ThreatInfo threat)
    {
        // 仮実装 ... 常に ver 0.8.1 のリーダー追従を行う
        //Pattern = Patterns.Follow01;

        // 仮実装 ver 0.8.3
        //   デフォルト = Follow01
        //   脅威が ThreatScanRadius 以内に接近 = Follow02
        //   脅威が格闘戦交戦距離 ( MeleeApproachMaxDistance ) まで接近 = Melee01
        if (threat.Target == null)
        {
            Pattern = Patterns.Follow01;
            return;
        }

        Pattern = self.GetDistanceSq(threat.Target) switch
        {
            var d when d <= Mathf.Pow(Cfg.MeleeApproachMaxDistance, 2.0f) => Patterns.Melee01,
            var d when d <= Mathf.Pow(Cfg.ThreatScanRadius, 2.0f) => Patterns.Follow02,
            _ => Patterns.Follow01
        };
        // TODO ( 遅い判断 )
        // n8n の戻り値を受けて Pattern の調整を行うステップの追加
    }

    internal enum Patterns
    {
        None,
        Follow01,
        Follow02,
        Melee01
    }
}