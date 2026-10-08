// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Text.Json.Serialization;
using Crank.PerfLabExporter.Contracts.PerfLab;

namespace Crank.PerfLabExporter.Contracts.Identity
{
    public sealed class ExportIdentity
    {
        [JsonPropertyName("build")]
        public PrimaryBuildIdentity Build { get; set; } = new();

        [JsonPropertyName("lane")]
        public LaneIdentity Lane { get; set; } = new();

        [JsonPropertyName("scenario")]
        public ScenarioIdentity Scenario { get; set; } = new();

        [JsonPropertyName("dependencies")]
        public List<DependencyMetadata> Dependencies { get; set; } = [];

        [JsonPropertyName("perfRepoHash")]
        public string PerfRepoHash { get; set; } = string.Empty;

        [JsonPropertyName("crankVersion")]
        public string CrankVersion { get; set; } = string.Empty;

        [JsonPropertyName("helixCorrelationId")]
        public string? HelixCorrelationId { get; set; }

        [JsonPropertyName("azureDevOps")]
        public AzureDevOpsMetadata AzureDevOps { get; set; } = new();

        [JsonPropertyName("sql")]
        public CrankSqlIdentity Sql { get; set; } = new();

        [JsonPropertyName("additionalData")]
        public Dictionary<string, string> AdditionalData { get; set; } = [];
    }

    public sealed class PrimaryBuildIdentity
    {
        [JsonPropertyName("repo")]
        public string Repo { get; set; } = string.Empty;

        [JsonPropertyName("branch")]
        public string Branch { get; set; } = string.Empty;

        [JsonPropertyName("gitHash")]
        public string GitHash { get; set; } = string.Empty;

        [JsonPropertyName("buildName")]
        public string BuildName { get; set; } = string.Empty;

        [JsonPropertyName("timeStamp")]
        public DateTimeOffset? TimeStamp { get; set; }

        [JsonPropertyName("version")]
        public string? Version { get; set; }

        [JsonPropertyName("artifactId")]
        public string? ArtifactId { get; set; }

        [JsonPropertyName("additionalData")]
        public Dictionary<string, string> AdditionalData { get; set; } = [];
    }

    public sealed class LaneIdentity
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("queue")]
        public string Queue { get; set; } = string.Empty;

        [JsonPropertyName("os")]
        public PerfLabOs Os { get; set; } = new();

        [JsonPropertyName("configurations")]
        public Dictionary<string, string> Configurations { get; set; } = [];

        [JsonPropertyName("hidden")]
        public bool Hidden { get; set; }

        [JsonPropertyName("additionalData")]
        public Dictionary<string, string> AdditionalData { get; set; } = [];
    }

    public sealed class ScenarioIdentity
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("family")]
        public string Family { get; set; } = string.Empty;

        [JsonPropertyName("categories")]
        public List<string> Categories { get; set; } = [];

        [JsonPropertyName("additionalData")]
        public Dictionary<string, string> AdditionalData { get; set; } = [];
    }

    public sealed class DependencyMetadata
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("aliases")]
        public List<string> Aliases { get; set; } = [];

        [JsonPropertyName("repository")]
        public string Repository { get; set; } = string.Empty;

        [JsonPropertyName("branch")]
        public string? Branch { get; set; }

        [JsonPropertyName("version")]
        public string? Version { get; set; }

        [JsonPropertyName("commitHash")]
        public string? CommitHash { get; set; }

        [JsonPropertyName("commitTimeStamp")]
        public DateTimeOffset? CommitTimeStamp { get; set; }

        [JsonPropertyName("additionalData")]
        public Dictionary<string, string> AdditionalData { get; set; } = [];
    }

    public sealed class AzureDevOpsMetadata
    {
        [JsonPropertyName("project")]
        public string Project { get; set; } = string.Empty;

        [JsonPropertyName("pipeline")]
        public string Pipeline { get; set; } = string.Empty;

        [JsonPropertyName("buildId")]
        public string BuildId { get; set; } = string.Empty;

        [JsonPropertyName("buildNumber")]
        public string BuildNumber { get; set; } = string.Empty;

        [JsonPropertyName("buildUrl")]
        public string BuildUrl { get; set; } = string.Empty;
    }

    public sealed class CrankSqlIdentity
    {
        [JsonPropertyName("session")]
        public string Session { get; set; } = string.Empty;

        [JsonPropertyName("table")]
        public string? Table { get; set; }

        [JsonPropertyName("recordId")]
        public string? RecordId { get; set; }
    }
}
