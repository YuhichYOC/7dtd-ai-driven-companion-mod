/*
 *
 * TriggerOperator.cs
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
using Logger = CompanionAIVerify.Log.Logger;

namespace CompanionAIVerify.Combat.Operation;

internal class TriggerOperator
{
    private readonly InfoHolder _i;

    private int _mag;

    private float _nextFireTime;

    internal TriggerOperator(InfoHolder i)
    {
        _i = i;
    }

    // 前フレームで press した -> 今フレーム release
    internal bool Pressed { get; set; }

    // ★ ( 3 ) フルオート判定して駆動
    internal void Run()
    {
        _mag = _i.GetHoldingMeta();
        if (Cfg.FullAutoHold && _i.FullAutoValidator.IsFullAuto())
        {
            if (_mag == 0)
                FullAutoReload();
            else
                FullAutoFire();
        }
        else
        {
            SemiAutoFire();
        }
    }

    // ★ v0.6.1 フルオート マガジン空 リロード
    // リロードは release エッジ ( bReleased ) が要る ( ItemActionRanged : 1236 `if (bReleased) ~ if (CanReload) requestReload` )
    //   フルオートは hold で離さないため bReleased が立たず自動リロードしない
    //   -> 空の間だけ release -> press を交互に打ってエッジを作り、リロードを発火させる
    //     CanReload に ADS ゲートは無い = ItemActionRanged : 872 -> ADS 解除は不要
    private void FullAutoReload()
    {
        if (Pressed)
        {
            _i.Self.Attack(true);
            Pressed = false;
        } // release ( bReleased 立て )
        else
        {
            _i.Self.Attack(false);
            Pressed = true;
        } // press ( empty -> requestReload )

        Logger.LogFire(_i, true, _i.GetHoldingMeta(), _mag);
    }

    // フルオート マガジン弾あり 発砲
    // トリガー保持で RPM 連射 ( Delay がケイデンスを律速 )
    // 離しは disengage 時のみ
    private void FullAutoFire()
    {
        Logger.LogRayProbe(_i); // [診断] 追加 : 発射直前に攻撃レイを実測
        _i.Self.Attack(false);
        Pressed = true;
        Logger.LogFire(_i, true, _i.GetHoldingMeta(), _mag);
    }

    // セミ & バースト
    // press(N) -> release(N+1) を FireInterval ごと
    private void SemiAutoFire()
    {
        if (Pressed)
        {
            _i.Self.Attack(true);
            Pressed = false;
            return;
        }

        if (Time.time < _nextFireTime) return;
        var before = _i.GetHoldingMeta();
        Logger.LogRayProbe(_i); // [診断] 追加 : 発射直前に攻撃レイを実測
        _i.Self.Attack(false);
        Pressed = true;
        _nextFireTime = Time.time + Cfg.RangedFireIntervalSec;
        Logger.LogFire(_i, false, _i.GetHoldingMeta(), before);
    }
}