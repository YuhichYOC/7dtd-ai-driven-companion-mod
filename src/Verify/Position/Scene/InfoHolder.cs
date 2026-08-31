/*
 *
 * InfoHolder.cs
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

using CompanionAIVerify.Perception;
using CompanionAIVerify.Position.Detection;
using CompanionAIVerify.Position.Operation;
using CompanionAIVerify.Position.Validation;

namespace CompanionAIVerify.Position.Scene;

internal class InfoHolder
{
    internal InfoHolder()
    {
        ObstacleDetector = new ObstacleDetector(this);
        FaceOperator = new FaceOperator(this);
        HaltOperator = new HaltOperator(this);
        SteerOperator = new SteerOperator(this);
        PositionValidator = new PositionValidator(this);
    }

    internal EntityPlayerLocal Self { get; set; }

    internal EntityPlayer Leader { get; set; }

    internal ThreatInfo Threat { get; set; }

    internal ObstacleDetector ObstacleDetector { get; }

    internal FaceOperator FaceOperator { get; }

    internal HaltOperator HaltOperator { get; }

    internal SteerOperator SteerOperator { get; }

    internal PositionValidator PositionValidator { get; }
}