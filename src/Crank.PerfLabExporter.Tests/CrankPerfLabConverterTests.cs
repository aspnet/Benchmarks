// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Text.Json;
using Crank.PerfLabExporter.Contracts.PerfLab;
using Crank.PerfLabExporter.Conversion;

namespace Crank.PerfLabExporter.Tests
{
    public class CrankPerfLabConverterTests
    {
        public static TheoryData<string, bool, bool, bool>
            RequestRejectionLatencyCases =>
            new()
            {
                {
                    "RejectionEncodedUrlHttpSys",
                    false,
                    true,
                    false
                },
                {
                    "RejectionInvalidHeaderHttpSys",
                    true,
                    false,
                    false
                },
                {
                    "RejectionHostHeaderMismatchHttpSys",
                    true,
                    true,
                    true
                },
                {
                    "RejectionEncodedUrlKestrel",
                    false,
                    true,
                    false
                },
                {
                    "RejectionInvalidHeaderKestrel",
                    true,
                    false,
                    false
                },
                {
                    "RejectionHostHeaderMismatchKestrel",
                    true,
                    true,
                    true
                }
            };

        [Fact]
        public async Task ConvertsFiveMonitoredCountersAndEveryOtherTopLevelScalar()
        {
            var conversion = await ConvertFixtureAsync();
            var report = conversion.Report;

            var test = Assert.Single(report.Tests);
            Assert.Equal("Plaintext", test.Name);
            Assert.Equal(7, test.Counters.Count);
            Assert.Equal(5, test.Counters.Count(counter => counter.TopCounter));
            var defaultCounter = Assert.Single(test.Counters, counter => counter.DefaultCounter);
            Assert.Equal("Requests/sec", defaultCounter.Name);
            Assert.True(defaultCounter.TopCounter);
            Assert.Equal(100_000, Assert.Single(defaultCounter.Results!));

            AssertCounter(test, "Mean latency", "ms", false, 1.25);
            AssertCounter(test, "P99 latency", "ms", false, 3.5);
            AssertCounter(test, "Startup time", "ms", false, 125.5);
            AssertCounter(test, "Published size", "bytes", false, 2_097_152);

            var unmapped = test.Counters.Single(
                counter => counter.Name == "jobs.load.results['custom/scalar']");
            Assert.False(unmapped.TopCounter);
            Assert.False(unmapped.DefaultCounter);
            Assert.False(unmapped.HigherIsBetter);
            Assert.Equal("value", unmapped.MetricName);
            Assert.Equal(12.75, Assert.Single(unmapped.Results!));
            Assert.DoesNotContain(
                test.Counters,
                counter => counter.Name.Contains("custom/nested", StringComparison.Ordinal));
            Assert.DoesNotContain(
                test.Counters,
                counter => counter.Name.Contains("custom/raw-array", StringComparison.Ordinal));

            Assert.Equal("single-aggregate-from-crank-json", test.AdditionalData["crank.sampleModel"]);
            Assert.Equal("1", test.AdditionalData["crank.independentSampleCount"]);
            Assert.Equal("false", test.AdditionalData["crank.measurementsUsedAsSamples"]);
            Assert.Equal("3", test.AdditionalData["crank.measurementPointCount"]);
            Assert.DoesNotContain(
                test.Counters,
                counter => counter.Name.Contains("measurements", StringComparison.Ordinal));
            Assert.Equal(5, conversion.Diagnostics.Count);
            Assert.Single(
                conversion.Diagnostics,
                diagnostic => diagnostic.Contains(
                    "jobs.application.results['custom/nested']",
                    StringComparison.Ordinal));
            Assert.Single(
                conversion.Diagnostics,
                diagnostic => diagnostic.Contains(
                    "jobs.application.results['custom/raw-array']",
                    StringComparison.Ordinal));
            Assert.Contains(
                conversion.Diagnostics,
                diagnostic => diagnostic.Contains("top-level JSON kind Object", StringComparison.Ordinal));
            Assert.Contains(
                conversion.Diagnostics,
                diagnostic => diagnostic.Contains("top-level JSON kind Array", StringComparison.Ordinal));
            Assert.Contains(
                conversion.Diagnostics,
                diagnostic =>
                    diagnostic.Contains(
                        "jobs.application.results['description']",
                        StringComparison.Ordinal) &&
                    diagnostic.Contains("top-level JSON kind String", StringComparison.Ordinal));
        }

        [Theory]
        [MemberData(nameof(RequestRejectionLatencyCases))]
        public async Task AppliesLatencyPolicyToEveryRequestRejectionScenario(
            string scenarioName,
            bool emitsP99,
            bool expectMean,
            bool expectP99)
        {
            var execution = FixtureLoader.LoadExecution();
            execution.JobResults.Properties["scenario"] = scenarioName;
            if (!emitsP99)
            {
                execution.JobResults.Jobs["load"].Results.Remove(
                    "http/latency/99");
            }

            var identity = FixtureLoader.LoadIdentity();
            identity.Scenario.Name = scenarioName;
            identity.Scenario.Family = "aspnet-request-rejection";
            var converter = new CrankPerfLabConverter(
                new StubCommitTimeResolver(
                    DateTimeOffset.Parse("2000-01-01T00:00:00Z")));

            var conversion = await converter.ConvertAsync(
                execution,
                FixtureLoader.LoadPolicy(),
                identity,
                CreateSource());

            var test = Assert.Single(conversion.Report.Tests);
            Assert.Equal(scenarioName, test.Name);
            var defaultCounter = Assert.Single(
                test.Counters,
                counter => counter.DefaultCounter);
            Assert.Equal("Requests/sec", defaultCounter.Name);
            if (expectMean)
            {
                AssertCounter(test, "Mean latency", "ms", false, 1.25);
            }
            else
            {
                Assert.DoesNotContain(
                    test.Counters,
                    counter => counter.Name == "Mean latency");
            }

            if (expectP99)
            {
                AssertCounter(test, "P99 latency", "ms", false, 3.5);
            }
            else
            {
                Assert.DoesNotContain(
                    test.Counters,
                    counter => counter.Name == "P99 latency");
            }

            Assert.Contains(
                test.Counters,
                counter => counter.Name == "Startup time");
            Assert.Contains(
                test.Counters,
                counter => counter.Name == "Published size");
            Assert.Contains(
                test.Counters,
                counter => counter.Name ==
                    "jobs.load.results['custom/scalar']");
            var omittedDiagnostics = conversion.Diagnostics
                .Where(diagnostic => diagnostic.Contains(
                    "Mapped numeric Crank result omitted",
                    StringComparison.Ordinal))
                .ToList();
            Assert.Equal(
                (expectMean ? 0 : 1) +
                (emitsP99 && !expectP99 ? 1 : 0),
                omittedDiagnostics.Count);
            Assert.All(
                omittedDiagnostics,
                diagnostic =>
                {
                    Assert.Contains(
                        scenarioName,
                        diagnostic,
                        StringComparison.Ordinal);
                    Assert.Contains(
                        "aspnet-request-rejection",
                        diagnostic,
                        StringComparison.Ordinal);
                });
        }

        [Fact]
        public async Task ResolvesRuntimeByNormalizedIdentityAndRecordsDependencyMetadata()
        {
            var conversion = await ConvertFixtureAsync();
            var report = conversion.Report;

            Assert.Equal("https://github.com/dotnet/runtime", report.Build.Repo);
            Assert.Equal("1111111111111111111111111111111111111111", report.Build.GitHash);
            Assert.Equal(DateTimeOffset.Parse("2026-08-18T11:59:00Z"), report.Build.TimeStamp);
            Assert.Equal(
                "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                report.Build.AdditionalData["aspnetCoreGitHash"]);
            Assert.Equal(
                "10.0.0-preview.7.25380.108",
                report.Build.AdditionalData["aspnetCoreVersion"]);
            Assert.Equal("12345", report.Build.AdditionalData["azureDevOpsBuildId"]);
            Assert.Equal(
                "2222222222222222222222222222222222222222",
                report.Build.AdditionalData["benchmarksGitHash"]);
            Assert.Equal("0.2.0-alpha.25380.1", report.Build.AdditionalData["crankVersion"]);

            using var dependencies = JsonDocument.Parse(report.Build.AdditionalData["dependencies"]);
            var dependencyNames = dependencies.RootElement
                .EnumerateArray()
                .Select(dependency => dependency.GetProperty("name").GetString())
                .ToList();
            Assert.Contains("runtime", dependencyNames);
            Assert.Contains("aspnetcore", dependencyNames);
            Assert.Contains("example", dependencyNames);

            Assert.Equal("aspnet-plaintext", report.Run.Name);
            Assert.Equal("Ubuntu.2204.Amd64.AspNetGold.Perf", report.Run.Queue);
            Assert.Null(report.Run.CorrelationId);
            Assert.Equal(
                ["Configuration", "Cores", "Framework", "Runtime"],
                report.Run.Configurations.Keys);
            Assert.Equal(
                "2222222222222222222222222222222222222222",
                report.Run.PerfRepoHash);

            var additionalData = Assert.Single(report.Tests).AdditionalData;
            Assert.Equal("trend-20260818-plaintext", additionalData["crank.sqlSession"]);
            Assert.Equal("TrendBenchmarks", additionalData["crank.sqlTable"]);
            Assert.Equal("987654", additionalData["crank.sqlRecordId"]);
            Assert.EndsWith("crank-result.json", additionalData["crank.resultPath"], StringComparison.Ordinal);
            Assert.EndsWith("counter-policy.json", additionalData["crank.counterPolicyPath"], StringComparison.Ordinal);
            Assert.EndsWith("export-identity.json", additionalData["crank.exportIdentityPath"], StringComparison.Ordinal);
        }

        [Fact]
        public async Task UsesInjectedCommitTimeResolverWhenIdentityTimestampIsMissing()
        {
            var identity = FixtureLoader.LoadIdentity();
            identity.Build.TimeStamp = null;
            var expected = DateTimeOffset.Parse("2026-08-18T09:30:00Z");
            var resolver = new StubCommitTimeResolver(expected);
            var converter = new CrankPerfLabConverter(resolver);

            var result = await converter.ConvertAsync(
                FixtureLoader.LoadExecution(),
                FixtureLoader.LoadPolicy(),
                identity,
                CreateSource());

            Assert.Equal(expected, result.Report.Build.TimeStamp);
            Assert.Equal(1, resolver.CallCount);
            Assert.Equal("https://github.com/dotnet/runtime", resolver.Repository);
            Assert.Equal("1111111111111111111111111111111111111111", resolver.CommitHash);
        }

        [Fact]
        public async Task RejectsMissingRuntimeDependencyEvenWhenBuildIdentityIsPresent()
        {
            var execution = FixtureLoader.LoadExecution();
            execution.JobResults.Jobs["application"].Dependencies.RemoveAll(dependency =>
                dependency.Names.Any(name =>
                    name.Equals("Microsoft.NETCore.App", StringComparison.OrdinalIgnoreCase)));
            var converter = new CrankPerfLabConverter(
                new StubCommitTimeResolver(DateTimeOffset.UtcNow));

            var exception = await Assert.ThrowsAsync<CrankConversionException>(() =>
                converter.ConvertAsync(
                    execution,
                    FixtureLoader.LoadPolicy(),
                    FixtureLoader.LoadIdentity(),
                    CreateSource()));

            Assert.Contains("does not contain a Microsoft.NETCore.App", exception.Message, StringComparison.Ordinal);
        }

        [Fact]
        public async Task RejectsMissingAspNetCoreDependencyMetadata()
        {
            var execution = FixtureLoader.LoadExecution();
            execution.JobResults.Jobs["application"].Dependencies.RemoveAll(dependency =>
                dependency.Names.Any(name =>
                    name.Equals("Microsoft.AspNetCore.App", StringComparison.OrdinalIgnoreCase)));
            var converter = new CrankPerfLabConverter(
                new StubCommitTimeResolver(DateTimeOffset.UtcNow));

            var exception = await Assert.ThrowsAsync<CrankConversionException>(() =>
                converter.ConvertAsync(
                    execution,
                    FixtureLoader.LoadPolicy(),
                    FixtureLoader.LoadIdentity(),
                    CreateSource()));

            Assert.Contains("does not contain a Microsoft.AspNetCore.App", exception.Message, StringComparison.Ordinal);
        }

        [Fact]
        public async Task RejectsAndDiagnosesNonFiniteResultScalar()
        {
            var execution = FixtureLoader.LoadExecution();
            using var nonFinite = JsonDocument.Parse("\"NaN\"");
            execution.JobResults.Jobs["load"].Results["custom/non-finite"] =
                nonFinite.RootElement.Clone();
            var converter = new CrankPerfLabConverter(
                new StubCommitTimeResolver(DateTimeOffset.UtcNow));

            var exception = await Assert.ThrowsAsync<CrankConversionException>(() =>
                converter.ConvertAsync(
                    execution,
                    FixtureLoader.LoadPolicy(),
                    FixtureLoader.LoadIdentity(),
                    CreateSource()));

            Assert.Contains("non-finite", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(
                "jobs.load.results['custom/non-finite']",
                exception.Message,
                StringComparison.Ordinal);
        }

        [Fact]
        public async Task RejectsReportWithNoFiniteTopLevelScalar()
        {
            var execution = FixtureLoader.LoadExecution();
            using var structured = JsonDocument.Parse("{\"nested\":123}");
            foreach (var job in execution.JobResults.Jobs.Values)
            {
                foreach (var resultName in job.Results.Keys.ToList())
                {
                    job.Results[resultName] = structured.RootElement.Clone();
                }
            }

            var converter = new CrankPerfLabConverter(
                new StubCommitTimeResolver(DateTimeOffset.UtcNow));

            var exception = await Assert.ThrowsAsync<CrankConversionException>(() =>
                converter.ConvertAsync(
                    execution,
                    FixtureLoader.LoadPolicy(),
                    FixtureLoader.LoadIdentity(),
                    CreateSource()));

            Assert.Contains("does not contain any finite top-level numeric result scalars", exception.Message, StringComparison.Ordinal);
            Assert.Contains("top-level JSON kind Object", exception.Message, StringComparison.Ordinal);
        }

        [Fact]
        public async Task RejectsResultWithoutConfiguredDefaultCounter()
        {
            var execution = FixtureLoader.LoadExecution();
            execution.JobResults.Jobs["load"].Results.Remove(
                "http/rps/mean");
            var converter = new CrankPerfLabConverter(
                new StubCommitTimeResolver(DateTimeOffset.UtcNow));

            var exception = await Assert.ThrowsAsync<CrankConversionException>(
                () => converter.ConvertAsync(
                    execution,
                    FixtureLoader.LoadPolicy(),
                    FixtureLoader.LoadIdentity(),
                    CreateSource()));

            Assert.Contains("default counter", exception.Message);
        }

        private static async Task<CrankConversionResult> ConvertFixtureAsync()
        {
            var converter = new CrankPerfLabConverter(
                new StubCommitTimeResolver(DateTimeOffset.Parse("2000-01-01T00:00:00Z")));
            return await converter.ConvertAsync(
                FixtureLoader.LoadExecution(),
                FixtureLoader.LoadPolicy(),
                FixtureLoader.LoadIdentity(),
                CreateSource());
        }

        private static ExportSourceMetadata CreateSource()
        {
            return new ExportSourceMetadata(
                FixtureLoader.GetPath("crank-result.json"),
                FixtureLoader.GetPath("counter-policy.json"),
                FixtureLoader.GetPath("export-identity.json"));
        }

        private static void AssertCounter(
            PerfLabTest test,
            string name,
            string metricName,
            bool higherIsBetter,
            double value)
        {
            var counter = test.Counters.Single(candidate => candidate.Name == name);
            Assert.True(counter.TopCounter);
            Assert.False(counter.DefaultCounter);
            Assert.Equal(higherIsBetter, counter.HigherIsBetter);
            Assert.Equal(metricName, counter.MetricName);
            Assert.Equal(value, Assert.Single(counter.Results!));
        }

        private sealed class StubCommitTimeResolver : ICommitTimeResolver
        {
            private readonly DateTimeOffset _timestamp;

            public StubCommitTimeResolver(DateTimeOffset timestamp)
            {
                _timestamp = timestamp;
            }

            public int CallCount { get; private set; }

            public string? Repository { get; private set; }

            public string? CommitHash { get; private set; }

            public Task<DateTimeOffset> ResolveAsync(
                string repository,
                string commitHash,
                CancellationToken cancellationToken)
            {
                CallCount++;
                Repository = repository;
                CommitHash = commitHash;
                return Task.FromResult(_timestamp);
            }
        }
    }
}
