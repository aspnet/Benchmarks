// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Crank.PerfLabExporter.Contracts
{
    public static class ContractJson
    {
        public static JsonSerializerOptions CreateSerializerOptions(bool writeIndented = false)
        {
            return new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
                DefaultIgnoreCondition = JsonIgnoreCondition.Never,
                NumberHandling = JsonNumberHandling.Strict,
                PropertyNameCaseInsensitive = false,
                WriteIndented = writeIndented
            };
        }
    }
}
