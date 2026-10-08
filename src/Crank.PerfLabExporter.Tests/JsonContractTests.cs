// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Text.Json;
using Crank.PerfLabExporter.Contracts;
using Crank.PerfLabExporter.Contracts.Crank;
using Crank.PerfLabExporter.Contracts.Identity;
using Crank.PerfLabExporter.Contracts.PerfLab;
using Crank.PerfLabExporter.Contracts.Policy;

namespace Crank.PerfLabExporter.Tests
{
    public class JsonContractTests
    {
        private static readonly JsonSerializerOptions SerializerOptions = ContractJson.CreateSerializerOptions();

        [Fact]
        public void ReadsCurrentCrankExecutionAndJobResultNames()
        {
            const string json =
                """
                {
                  "returnCode": 0,
                  "jobResults": {
                    "jobs": {
                      "load": {
                        "results": {
                          "http/rps/mean": 123.5,
                          "custom/scalar": 42
                        },
                        "metadata": [
                          {
                            "name": "http/rps/mean",
                            "description": "Requests per second",
                            "format": "n0"
                          }
                        ],
                        "dependencies": [
                          {
                            "id": "Microsoft.AspNetCore.App",
                            "names": [ "Microsoft.AspNetCore.App" ],
                            "repositoryUrl": "https://github.com/dotnet/aspnetcore",
                            "version": "10.0.0-preview",
                            "commitHash": "abcdef"
                          }
                        ],
                        "measurements": [],
                        "environment": {
                          "architecture": "x64"
                        },
                        "variables": {}
                      }
                    },
                    "properties": {
                      "buildId": "42"
                    }
                  },
                  "benchmarks": []
                }
                """;

            var execution = JsonSerializer.Deserialize<CrankExecutionResult>(json, SerializerOptions);

            Assert.NotNull(execution);
            Assert.Equal(0, execution.ReturnCode);
            var load = execution.JobResults.Jobs["load"];
            Assert.Equal(123.5, load.Results["http/rps/mean"].GetDouble());
            Assert.Equal(42, load.Results["custom/scalar"].GetInt32());
            Assert.Equal("abcdef", load.Dependencies[0].CommitHash);
            Assert.Equal("42", execution.JobResults.Properties["buildId"]);
        }

        [Fact]
        public void WritesExistingPerfLabNamesAndNewThreshold()
        {
            var json = JsonSerializer.Serialize(ContractTestData.CreateValidReport(), SerializerOptions);
            using var document = JsonDocument.Parse(json);

            var root = document.RootElement;
            Assert.True(root.TryGetProperty("build", out _));
            Assert.True(root.TryGetProperty("os", out _));
            Assert.True(root.TryGetProperty("run", out _));
            var counter = root.GetProperty("tests")[0].GetProperty("counters")[0];
            Assert.True(counter.GetProperty("topCounter").GetBoolean());
            Assert.True(counter.GetProperty("defaultCounter").GetBoolean());
            Assert.True(counter.GetProperty("higherIsBetter").GetBoolean());
            Assert.Equal("requests/sec", counter.GetProperty("metricName").GetString());
            Assert.Equal(0.02, counter.GetProperty("regressionThreshold").GetDouble());
            var unmappedCounter = root.GetProperty("tests")[0].GetProperty("counters")[1];
            Assert.Equal(JsonValueKind.False, unmappedCounter.GetProperty("higherIsBetter").ValueKind);
            Assert.False(unmappedCounter.TryGetProperty("regressionThreshold", out _));
        }

        [Fact]
        public void DefaultsOmittedDirectionToFalseAndKeepsThresholdOptional()
        {
            const string json =
                """
                {
                  "name": "jobs.load.results['custom/scalar']",
                  "topCounter": false,
                  "defaultCounter": false,
                  "metricName": "value",
                  "results": [ 42 ]
                }
                """;

            var counter = JsonSerializer.Deserialize<PerfLabCounter>(json, SerializerOptions);

            Assert.NotNull(counter);
            Assert.False(counter.HigherIsBetter);
            Assert.Null(counter.RegressionThreshold);

            var serialized = JsonSerializer.Serialize(counter, SerializerOptions);
            Assert.Contains("\"higherIsBetter\":false", serialized, StringComparison.Ordinal);
            Assert.DoesNotContain("regressionThreshold", serialized, StringComparison.Ordinal);
        }

        [Fact]
        public void AppliesSimpleDefaultsToPolicyMappings()
        {
            const string json =
                """
                {
                  "schemaVersion": 1,
                  "mappings": [
                    {
                      "path": "jobs.load.results['http/rps/mean']",
                      "name": "Requests/sec",
                      "metricName": "requests/sec",
                      "higherIsBetter": true,
                      "topCounter": true,
                      "defaultCounter": true
                    }
                  ]
                }
                """;

            var policy = JsonSerializer.Deserialize<CounterPolicy>(json, SerializerOptions);

            Assert.NotNull(policy);
            Assert.Null(policy.Mappings[0].RegressionThreshold);
            Assert.Equal(1, policy.Mappings[0].Scale);
            Assert.Empty(policy.Mappings[0].ExcludedScenarios);
        }

        [Fact]
        public void RoundTripsExcludedScenarios()
        {
            var policy = ContractTestData.CreateValidPolicy();

            var json = JsonSerializer.Serialize(policy, SerializerOptions);
            var roundTrip = JsonSerializer.Deserialize<CounterPolicy>(
                json,
                SerializerOptions);

            Assert.NotNull(roundTrip);
            Assert.Equal(
                [
                    "RejectionInvalidHeaderHttpSys",
                    "RejectionInvalidHeaderKestrel"
                ],
                roundTrip.Mappings[1].ExcludedScenarios);
        }

        [Fact]
        public void DefaultPolicyExcludesOnlyPipeliningRejectionScenarios()
        {
            var policy = FixtureLoader.LoadPolicy();
            var latencyMappings = policy.Mappings
                .Where(mapping =>
                    mapping.Path.EndsWith(
                        "['http/latency/mean']",
                        StringComparison.Ordinal) ||
                    mapping.Path.EndsWith(
                        "['http/latency/99']",
                        StringComparison.Ordinal))
                .ToList();

            Assert.Equal(2, latencyMappings.Count);
            foreach (var mapping in latencyMappings)
            {
                Assert.Equal(
                    [
                        "RejectionInvalidHeaderHttpSys",
                        "RejectionInvalidHeaderKestrel"
                    ],
                    mapping.ExcludedScenarios);
            }
        }

        [Fact]
        public void WritesLaneFamilyAndDependencyMetadataNames()
        {
            var json = JsonSerializer.Serialize(ContractTestData.CreateValidIdentity(), SerializerOptions);
            using var document = JsonDocument.Parse(json);

            var root = document.RootElement;
            Assert.Equal("aspnet-gold-linux-x64", root.GetProperty("lane").GetProperty("name").GetString());
            Assert.Equal("aspnet-plaintext", root.GetProperty("scenario").GetProperty("family").GetString());
            Assert.Equal(
                "https://github.com/dotnet/aspnetcore",
                root.GetProperty("dependencies")[0].GetProperty("repository").GetString());
        }
    }
}
