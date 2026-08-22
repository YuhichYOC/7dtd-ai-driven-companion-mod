/*
*
* Engage.cs
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

namespace CompanionAIVerify.Combat
{
    internal static class Engage
    {
        // 保持アイテムの実効リーチ。EngageRange.Read が Dynamic melee ( = ItemActionDynamic.Range / RangeDefault ) を
        // 正しく解決する。旧実装は基底 ItemAction.Range を読んでいたため、Dynamic melee のリーチを取れず
        // 2.4m 武器を 2.0m フォールバック扱いしていた ( 実ログで range = 2.4 と確認済み )。取れなければ 2.0m。
        private static float GetAttackReach(EntityPlayerLocal self)
        {
            EngageRange.Info er = EngageRange.Read(self);
            if (er.valid && er.range > 0.01f) return er.range;
            return 2.0f;
        }
    }
}