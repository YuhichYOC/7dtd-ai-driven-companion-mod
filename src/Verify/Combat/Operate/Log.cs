/*
*
* Log.cs
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
    internal class Log
    {
        private LogInfoHolder _i;

        internal Log(LogInfoHolder i)
        {
            _i = i;
        }

        internal void LogMeleeSwing(InfoHolder i)
        {
            if (Time.time >= _i.NextEngageLogTime)
            {
                _i.NextEngageLogTime = Time.time + Cfg.LogThrottleSec;
                Log.Out($"[CompanionAI] engage: swing {i.Target.Kind} {i.Target.State} d={i.Distance:0.0}m reach={i.Reach:0.0}m");
            }
        }
    }
}
