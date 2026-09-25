// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Crank.PerfLabExporter.Contracts.Crank
{
    public sealed class CrankExecutionResult
    {
        [JsonPropertyName("returnCode")]
        public int ReturnCode { get; set; }

        [JsonPropertyName("crankVersion")]
        public string? CrankVersion { get; set; }

        [JsonPropertyName("jobResults")]
        public CrankJobResults JobResults { get; set; } = new();

        [JsonPropertyName("benchmarks")]
        public List<JsonElement> Benchmarks { get; set; } = [];
    }

    public sealed class CrankJobResults
    {
        [JsonPropertyName("jobs")]
        public Dictionary<string, CrankJobResult> Jobs { get; set; } = [];

        [JsonPropertyName("properties")]
        public Dictionary<string, string> Properties { get; set; } = [];
    }

    public sealed class CrankJobResult
    {
        [JsonPropertyName("results")]
        public Dictionary<string, JsonElement> Results { get; set; } = [];

        [JsonPropertyName("metadata")]
        public List<CrankResultMetadata> Metadata { get; set; } = [];

        [JsonPropertyName("dependencies")]
        public List<CrankDependency> Dependencies { get; set; } = [];

        [JsonPropertyName("measurements")]
        public List<List<CrankMeasurement>> Measurements { get; set; } = [];

        [JsonPropertyName("environment")]
        public Dictionary<string, JsonElement> Environment { get; set; } = [];

        [JsonPropertyName("variables")]
        public Dictionary<string, JsonElement> Variables { get; set; } = [];

        [JsonPropertyName("benchmarks")]
        public List<JsonElement> Benchmarks { get; set; } = [];
    }

    public sealed class CrankResultMetadata
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("description")]
        public string Description { get; set; } = string.Empty;

        [JsonPropertyName("format")]
        public string Format { get; set; } = string.Empty;
    }

    public sealed class CrankDependency
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("names")]
        public List<string> Names { get; set; } = [];

        [JsonPropertyName("repositoryUrl")]
        public string RepositoryUrl { get; set; } = string.Empty;

        [JsonPropertyName("version")]
        public string Version { get; set; } = string.Empty;

        [JsonPropertyName("commitHash")]
        public string CommitHash { get; set; } = string.Empty;
    }

    public sealed class CrankMeasurement
    {
        [JsonPropertyName("timestamp")]
        public DateTimeOffset Timestamp { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("value")]
        public JsonElement Value { get; set; }
    }
}
