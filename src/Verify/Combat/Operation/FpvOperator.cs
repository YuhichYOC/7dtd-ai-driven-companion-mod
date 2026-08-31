/*
 *
 * FpvOperator.cs
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
using CompanionAIVerify.Log;

namespace CompanionAIVerify.Combat.Operation;

internal class FpvOperator
{
    private readonly InfoHolder _i;
    private bool _fpvLogged;

    private bool _lastFpv;

    internal FpvOperator(InfoHolder i)
    {
        _i = i;
        _fpvLogged = false;
    }

    // 交戦の手前で bFirstPersonView を実ログ
    // 初回 or 変化時のみ出力
    internal void Run()
    {
        var fpv = _i.Self.bFirstPersonView;
        if (_fpvLogged && fpv == _lastFpv) return;

        _fpvLogged = true;
        _lastFpv = fpv;
        Logger.LogFpv(_i, fpv);

        if (!fpv && Cfg.ForceFirstPerson)
        {
            _i.Self.SetFirstPersonView(true, false); // spawn 経路の誤設定を自己修復
            Logger.LogFpv(_i);
        }
    }
}