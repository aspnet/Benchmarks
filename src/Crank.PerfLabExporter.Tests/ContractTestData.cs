// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Crank.PerfLabExporter.Contracts.Identity;
using Crank.PerfLabExporter.Contracts.PerfLab;
using Crank.PerfLabExporter.Contracts.Policy;

namespace Crank.PerfLabExporter.Tests
{
    internal static class ContractTestData
    {
        public static PerfLabReport CreateValidReport()
        {
            return new PerfLabReport
            {
                Build = new PerfLabBuild
                {
                    Repo = "https://github.com/dotnet/runtime",
                    Branch = "main",
                    Architecture = "x64",
                    Locale = "en-US",
                    GitHash = "0123456789abcdef",
                    BuildName = "runtime-build",
                    TimeStamp = DateTimeOffset.Parse("2026-08-18T12:00:00Z")
                },
                Os = new PerfLabOs
                {
                    Name = "Ubuntu 22.04",
                    Architecture = "x64",
                    Locale = "en-US"
                },
                Run = new PerfLabRun
                {
                    Name = "aspnet-plaintext",
                    Queue = "Ubuntu.2204.Amd64.AspNetGold.Perf",
                    PerfRepoHash = "fedcba9876543210"
                },
                Tests =
                [
                    new PerfLabTest
                    {
                        Name = "Plaintext",
                        Counters =
                        [
                            new PerfLabCounter
                            {
                                Name = "Requests/sec",
                                TopCounter = true,
                                DefaultCounter = true,
                                HigherIsBetter = true,
                                MetricName = "requests/sec",
                                Results = [100_000],
                                RegressionThreshold = 0.02
                            },
                            new PerfLabCounter
                            {
                                Name = "jobs.load.results['custom/scalar']",
                                TopCounter = false,
                                DefaultCounter = false,
                                HigherIsBetter = false,
                                MetricName = "value",
                                Results = [42]
                            }
                        ]
                    }
                ]
            };
        }

        public static CounterPolicy CreateValidPolicy()
        {
            return new CounterPolicy
            {
                Mappings =
                [
                    new CounterMapping
                    {
                        Path = "jobs.load.results['http/rps/mean']",
                        Name = "Requests/sec",
                        MetricName = "requests/sec",
                        HigherIsBetter = true,
                        TopCounter = true,
                        DefaultCounter = true,
                        RegressionThreshold = 0.02
                    },
                    new CounterMapping
                    {
                        Path = "jobs.load.results['http/latency/99']",
                        Name = "P99 latency",
                        MetricName = "ms",
                        HigherIsBetter = false,
                        TopCounter = true,
                        RegressionThreshold = 0.05,
                        Scale = 0.001,
                        ExcludedScenarios =
                        [
                            "RejectionInvalidHeaderHttpSys",
                            "RejectionInvalidHeaderKestrel"
                        ]
                    }
                ]
            };
        }

        public static ExportIdentity CreateValidIdentity()
        {
            return new ExportIdentity
            {
                Build = new PrimaryBuildIdentity
                {
                    Repo = "https://github.com/dotnet/runtime",
                    Branch = "main",
                    GitHash = "0123456789abcdef",
                    BuildName = "runtime-build",
                    TimeStamp = DateTimeOffset.Parse("2026-08-18T12:00:00Z"),
                    Version = "10.0.0-preview"
                },
                Lane = new LaneIdentity
                {
                    Name = "aspnet-gold-linux-x64",
                    Queue = "Ubuntu.2204.Amd64.AspNetGold.Perf",
                    Os = new PerfLabOs
                    {
                        Name = "Ubuntu 22.04",
                        Architecture = "x64",
                        Locale = "en-US"
                    },
                    Configurations =
                    {
                        ["Framework"] = "net10.0",
                        ["Cores"] = "28"
                    }
                },
                Scenario = new ScenarioIdentity
                {
                    Name = "Plaintext",
                    Family = "aspnet-plaintext"
                },
                Dependencies =
                [
                    new DependencyMetadata
                    {
                        Name = "aspnetcore",
                        Aliases = ["Microsoft.AspNetCore.App"],
                        Repository = "https://github.com/dotnet/aspnetcore",
                        Version = "10.0.0-preview",
                        CommitHash = "abcdef0123456789"
                    }
                ],
                PerfRepoHash = "fedcba9876543210",
                CrankVersion = "0.2.0-alpha",
                AzureDevOps = new AzureDevOpsMetadata
                {
                    Project = "internal",
                    Pipeline = "aspnet-benchmarks",
                    BuildId = "42",
                    BuildNumber = "20260818.1",
                    BuildUrl = "https://dev.azure.com/example/internal/_build/results?buildId=42"
                },
                Sql = new CrankSqlIdentity
                {
                    Session = "trend-session"
                }
            };
        }
    }
}
