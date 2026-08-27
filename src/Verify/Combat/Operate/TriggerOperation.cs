/*
*
* TriggerOperation.cs
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
    internal class TriggerOperation
    {
        private InfoHolder _i;
        private LogInfoHolder _li;

        private float _nextFireTime;

        private bool _pressed;
        internal bool Pressed { set => _pressed = value; get => _pressed; }

        internal TriggerOperation(InfoHolder i, LogInfoHolder li)
        {
            _i = i;
            _li = li;
        }

        // ★ ( 3 ) フルオート判定して駆動
        internal void Run()
        {
            if (Cfg.FullAutoHold && _i.FullAutoValidator.IsFullAuto())
            {
                if (_i.GetHoldingMeta() == 0)
                {
                    FullAutoReload();
                }
                else
                {
                    FullAutoFire();
                }
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
            if (_i.FirePressed) { _i.Self.Attack(true);  _i.FirePressed = false; } // release ( bReleased 立て )
            else                { _i.Self.Attack(false); _i.FirePressed = true;  } // press ( empty -> requestReload )
        }

        // フルオート マガジン弾あり 発砲
        // トリガー保持で RPM 連射 ( Delay がケイデンスを律速 )
        // 離しは disengage 時のみ
        private void FullAutoFire()
        {
            _i.Self.Attack(false);
            _i.FirePressed = true;
        }

        // セミ & バースト
        // press(N) -> release(N+1) を FireInterval ごと
        private void SemiAutoFire()
        {
            if (_i.FirePressed)
            {
                _i.Self.Attack(true);
                _i.FirePressed = false;
                return;
            }
            if (Time.time < _nextFireTime)
            {
                return;
            }
            int before = _i.GetHoldingMeta();
            _i.Self.Attack(false);
            _i.FirePressed = true;
            _nextFireTime = Time.time + Cfg.RangedFireIntervalSec;
        }
    }
}
