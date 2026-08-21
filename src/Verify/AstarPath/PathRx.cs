/*
*
* PathRx.cs
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
*
*/

// =============================================================================
// navigation スライス2 : クライアント側チャット横取り（表示抑止＋再結合へ供給）
//
//   GameManager.ChatMessageClient(GameManager:4512) の prefix。
//   自タグ(~CAIP~)で始まるメッセージのみ:
//     - client(IsRemote) では PathWire.OnChunkClient へ渡して再結合。
//     - return false で XUiC_ChatOutput.AddMessage(GameManager:4522) を抑止し、チャット欄に出さない。
//   タグ無しの通常チャットは return true でそのまま流す。
//
//   注意: ホストでも ChatMessageServer(4481) が ChatMessageClient を呼ぶため本prefixは発火するが、
//   IsRemote==false のため OnChunkClient は呼ばず、抑止のみ行う（自分の送出を受信扱いしない）。
//   prefix が false を返しても ChatMessageClient の本体をスキップするだけで、呼び元
//   ChatMessageServer の後続（recipient へのパッケージ送出 4496-4504）には影響しない。
//
//   ChatMessageClient のパラメータ名は _msg（GameManager:4512）。Harmony は同名でバインドする。
// =============================================================================

using HarmonyLib;

namespace CompanionAIVerify.AstarPath
{
    [HarmonyPatch(typeof(GameManager), "ChatMessageClient")]
    internal static class Patch_GameManager_ChatMessageClient_PathRx
    {
        private static bool Prefix(string _msg)
        {
            if (string.IsNullOrEmpty(_msg) || !_msg.StartsWith(PathWire.Tag))
                return true;    // 通常チャット → 通す

            World world = (GameManager.Instance != null) ? GameManager.Instance.World : null;
            if (world != null && world.IsRemote())
                PathWire.OnChunkClient(_msg);   // クライアントでのみ再結合

            return false;       // 自タグは表示抑止
        }
    }
}
