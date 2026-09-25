// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Text.Json.Serialization;

namespace Crank.PerfLabExporter.Contracts.Policy
{
    public sealed class CounterPolicy
    {
        [JsonPropertyName("schemaVersion")]
        public int SchemaVersion { get; set; } = 1;

        [JsonPropertyName("mappings")]
        public List<CounterMapping> Mappings { get; set; } = [];
    }

    public sealed class CounterMapping
    {
        [JsonPropertyName("path")]
        public string Path { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("metricName")]
        public string MetricName { get; set; } = string.Empty;

        [JsonPropertyName("higherIsBetter")]
        public bool HigherIsBetter { get; set; }

        [JsonPropertyName("topCounter")]
        public bool TopCounter { get; set; }

        [JsonPropertyName("defaultCounter")]
        public bool DefaultCounter { get; set; }

        [JsonPropertyName("regressionThreshold")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public double? RegressionThreshold { get; set; }

        [JsonPropertyName("scale")]
        public double Scale { get; set; } = 1;

        [JsonPropertyName("excludedScenarios")]
        public List<string> ExcludedScenarios { get; set; } = [];
    }
}
