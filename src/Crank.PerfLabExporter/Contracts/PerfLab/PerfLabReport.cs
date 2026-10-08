// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Text.Json.Serialization;

namespace Crank.PerfLabExporter.Contracts.PerfLab
{
    public sealed class PerfLabReport
    {
        [JsonPropertyName("build")]
        public PerfLabBuild Build { get; set; } = new();

        [JsonPropertyName("os")]
        public PerfLabOs Os { get; set; } = new();

        [JsonPropertyName("run")]
        public PerfLabRun Run { get; set; } = new();

        [JsonPropertyName("tests")]
        public List<PerfLabTest> Tests { get; set; } = [];
    }

    public sealed class PerfLabBuild
    {
        [JsonPropertyName("repo")]
        public string Repo { get; set; } = string.Empty;

        [JsonPropertyName("branch")]
        public string Branch { get; set; } = string.Empty;

        [JsonPropertyName("architecture")]
        public string Architecture { get; set; } = string.Empty;

        [JsonPropertyName("locale")]
        public string Locale { get; set; } = string.Empty;

        [JsonPropertyName("gitHash")]
        public string GitHash { get; set; } = string.Empty;

        [JsonPropertyName("buildName")]
        public string BuildName { get; set; } = string.Empty;

        [JsonPropertyName("timeStamp")]
        public DateTimeOffset TimeStamp { get; set; }

        [JsonPropertyName("additionalData")]
        public Dictionary<string, string> AdditionalData { get; set; } = [];
    }

    public sealed class PerfLabOs
    {
        [JsonPropertyName("locale")]
        public string Locale { get; set; } = string.Empty;

        [JsonPropertyName("architecture")]
        public string Architecture { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("machineName")]
        public string? MachineName { get; set; }
    }

    public sealed class PerfLabRun
    {
        [JsonPropertyName("hidden")]
        public bool Hidden { get; set; }

        [JsonPropertyName("correlationId")]
        public string? CorrelationId { get; set; }

        [JsonPropertyName("perfRepoHash")]
        public string? PerfRepoHash { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("queue")]
        public string Queue { get; set; } = string.Empty;

        [JsonPropertyName("workItemName")]
        public string? WorkItemName { get; set; }

        [JsonPropertyName("configurations")]
        public Dictionary<string, string> Configurations { get; set; } = [];
    }

    public sealed class PerfLabTest
    {
        [JsonPropertyName("categories")]
        public List<string> Categories { get; set; } = [];

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("additionalData")]
        public Dictionary<string, string> AdditionalData { get; set; } = [];

        [JsonPropertyName("counters")]
        public List<PerfLabCounter> Counters { get; set; } = [];
    }

    public sealed class PerfLabCounter
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("topCounter")]
        public bool TopCounter { get; set; }

        [JsonPropertyName("defaultCounter")]
        public bool DefaultCounter { get; set; }

        [JsonPropertyName("higherIsBetter")]
        public bool HigherIsBetter { get; set; }

        [JsonPropertyName("metricName")]
        public string MetricName { get; set; } = string.Empty;

        [JsonPropertyName("results")]
        public List<double>? Results { get; set; }

        [JsonPropertyName("regressionThreshold")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public double? RegressionThreshold { get; set; }
    }
}
