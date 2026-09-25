// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Globalization;
using System.Text.Json;
using Crank.PerfLabExporter.Contracts;
using Crank.PerfLabExporter.Contracts.Crank;
using Crank.PerfLabExporter.Contracts.Identity;
using Crank.PerfLabExporter.Contracts.PerfLab;
using Crank.PerfLabExporter.Contracts.Policy;

namespace Crank.PerfLabExporter.Conversion
{
    public sealed record CrankConversionResult(
        PerfLabReport Report,
        IReadOnlyList<string> Diagnostics);

    public sealed class CrankPerfLabConverter
    {
        private const string SampleModel = "single-aggregate-from-crank-json";
        private readonly ICommitTimeResolver _commitTimeResolver;

        public CrankPerfLabConverter(ICommitTimeResolver commitTimeResolver)
        {
            _commitTimeResolver = commitTimeResolver;
        }

        public async Task<CrankConversionResult> ConvertAsync(
            CrankExecutionResult execution,
            CounterPolicy policy,
            ExportIdentity identity,
            ExportSourceMetadata source,
            CancellationToken cancellationToken = default)
        {
            ValidatePolicy(policy);

            var resolution = CrankDependencyResolver.Resolve(execution, identity);
            var enumeration = CrankScalarEnumerator.Enumerate(execution);
            var scalars = enumeration.Scalars;
            var diagnostics = enumeration.Diagnostics.ToList();
            if (scalars.Count == 0)
            {
                var diagnosticDetails = diagnostics.Count == 0
                    ? string.Empty
                    : Environment.NewLine + string.Join(Environment.NewLine, diagnostics.Select(diagnostic => $"  {diagnostic}"));
                throw new CrankConversionException(
                    "The Crank result does not contain any finite top-level numeric result scalars." +
                    diagnosticDetails);
            }

            if (resolution.Runtime.CommitTimeStamp is { } dependencyTimestamp &&
                identity.Build.TimeStamp is { } identityTimestamp &&
                dependencyTimestamp.ToUniversalTime() != identityTimestamp.ToUniversalTime())
            {
                throw new CrankConversionException(
                    "The primary build timestamp contradicts the resolved runtime dependency timestamp.");
            }

            var commitTimestamp = resolution.Runtime.CommitTimeStamp ?? identity.Build.TimeStamp;
            if (commitTimestamp is null)
            {
                commitTimestamp = await _commitTimeResolver.ResolveAsync(
                    resolution.Runtime.Repository,
                    resolution.Runtime.CommitHash!,
                    cancellationToken);
            }

            if (commitTimestamp == default)
            {
                throw new CrankConversionException("The resolved runtime commit timestamp is missing or default.");
            }

            var mappingLookup = policy.Mappings.ToDictionary(
                mapping => mapping.Path,
                StringComparer.Ordinal);
            var counters = new List<(PerfLabCounter Counter, string SourcePath, bool IsMapped)>();
            foreach (var scalar in scalars)
            {
                if (mappingLookup.TryGetValue(scalar.SourcePath, out var mapping))
                {
                    if (mapping.ExcludedScenarios?.Contains(
                            identity.Scenario.Name,
                            StringComparer.Ordinal) == true)
                    {
                        diagnostics.Add(
                            $"Mapped numeric Crank result omitted for scenario " +
                            $"'{identity.Scenario.Name}' in family " +
                            $"'{identity.Scenario.Family}': " +
                            $"{scalar.SourcePath}");
                        continue;
                    }

                    var normalized = Normalize(scalar.Value, mapping, scalar.SourcePath);
                    counters.Add((
                        new PerfLabCounter
                        {
                            Name = mapping.Name,
                            TopCounter = mapping.TopCounter,
                            DefaultCounter = mapping.DefaultCounter,
                            HigherIsBetter = mapping.HigherIsBetter,
                            MetricName = mapping.MetricName,
                            Results = [normalized],
                            RegressionThreshold = mapping.RegressionThreshold
                        },
                        scalar.SourcePath,
                        true));
                }
                else
                {
                    counters.Add((
                        new PerfLabCounter
                        {
                            Name = scalar.SourcePath,
                            TopCounter = false,
                            DefaultCounter = false,
                            HigherIsBetter = false,
                            MetricName = "value",
                            Results = [scalar.Value]
                        },
                        scalar.SourcePath,
                        false));
                    diagnostics.Add($"Unmapped numeric Crank result emitted as storage-only counter: {scalar.SourcePath}");
                }
            }

            var orderedCounters = counters
                .OrderByDescending(counter => counter.Counter.DefaultCounter)
                .ThenByDescending(counter => counter.Counter.TopCounter)
                .ThenBy(counter => counter.Counter.Name, StringComparer.Ordinal)
                .ToList();
            if (orderedCounters.Count(counter =>
                    counter.Counter.DefaultCounter) != 1)
            {
                throw new CrankConversionException(
                    "The Crank result does not contain the configured default counter.");
            }

            var testAdditionalData = CreateTestAdditionalData(
                execution,
                identity,
                source,
                orderedCounters);
            var report = new PerfLabReport
            {
                Build = new PerfLabBuild
                {
                    Repo = RepositoryIdentity.ToCanonicalUrl(resolution.Runtime.Repository),
                    Branch = identity.Build.Branch,
                    Architecture = identity.Lane.Os.Architecture,
                    Locale = identity.Lane.Os.Locale,
                    GitHash = resolution.Runtime.CommitHash!,
                    BuildName = identity.Build.BuildName,
                    TimeStamp = commitTimestamp.Value.ToUniversalTime(),
                    AdditionalData = CreateBuildAdditionalData(identity, resolution)
                },
                Os = new PerfLabOs
                {
                    Name = identity.Lane.Os.Name,
                    Architecture = identity.Lane.Os.Architecture,
                    Locale = identity.Lane.Os.Locale,
                    MachineName = identity.Lane.Os.MachineName
                },
                Run = new PerfLabRun
                {
                    Hidden = identity.Lane.Hidden,
                    CorrelationId = NormalizeCorrelationId(identity.HelixCorrelationId),
                    PerfRepoHash = identity.PerfRepoHash,
                    Name = identity.Scenario.Family,
                    Queue = identity.Lane.Queue,
                    WorkItemName = null,
                    Configurations = ToSortedDictionary(identity.Lane.Configurations)
                },
                Tests =
                [
                    new PerfLabTest
                    {
                        Name = identity.Scenario.Name,
                        Categories = identity.Scenario.Categories
                            .Where(category => !string.IsNullOrWhiteSpace(category))
                            .Distinct(StringComparer.Ordinal)
                            .OrderBy(category => category, StringComparer.Ordinal)
                            .ToList(),
                        AdditionalData = testAdditionalData,
                        Counters = orderedCounters.Select(counter => counter.Counter).ToList()
                    }
                ]
            };

            return new CrankConversionResult(report, diagnostics);
        }

        private static void ValidatePolicy(CounterPolicy policy)
        {
            if (policy.SchemaVersion != 1)
            {
                throw new CrankConversionException(
                    $"Unsupported counter policy schema version {policy.SchemaVersion}.");
            }

            if (policy.Mappings.Count(mapping => mapping.DefaultCounter) != 1)
            {
                throw new CrankConversionException(
                    "The counter policy must contain exactly one default counter.");
            }

            var duplicatePath = policy.Mappings
                .GroupBy(mapping => mapping.Path, StringComparer.Ordinal)
                .FirstOrDefault(group => group.Count() > 1);
            var duplicateName = policy.Mappings
                .GroupBy(mapping => mapping.Name, StringComparer.Ordinal)
                .FirstOrDefault(group => group.Count() > 1);
            if (duplicatePath is not null || duplicateName is not null)
            {
                throw new CrankConversionException(
                    "Counter policy paths and names must be unique.");
            }

            foreach (var mapping in policy.Mappings)
            {
                if (string.IsNullOrWhiteSpace(mapping.Path) ||
                    string.IsNullOrWhiteSpace(mapping.Name) ||
                    string.IsNullOrWhiteSpace(mapping.MetricName))
                {
                    throw new CrankConversionException(
                        "Counter policy mappings require a path, name, and metricName.");
                }

                if (mapping.DefaultCounter && !mapping.TopCounter)
                {
                    throw new CrankConversionException(
                        $"Default counter '{mapping.Name}' must also be a top counter.");
                }

                if (mapping.DefaultCounter &&
                    mapping.ExcludedScenarios?.Count > 0)
                {
                    throw new CrankConversionException(
                        "The default counter cannot exclude scenarios.");
                }

                if (!double.IsFinite(mapping.Scale) || mapping.Scale <= 0)
                {
                    throw new CrankConversionException(
                        $"Counter '{mapping.Name}' has an invalid scale.");
                }

                if (mapping.RegressionThreshold is { } threshold &&
                    (!double.IsFinite(threshold) || threshold <= 0 || threshold > 1))
                {
                    throw new CrankConversionException(
                        $"Counter '{mapping.Name}' has an invalid regression threshold.");
                }
            }
        }

        private static double Normalize(
            double value,
            CounterMapping mapping,
            string sourcePath)
        {
            if (mapping.Scale == 1)
            {
                return value;
            }

            var normalized = value * mapping.Scale;
            if (!double.IsFinite(normalized))
            {
                throw new CrankConversionException(
                    $"Normalization produced a non-finite value for '{sourcePath}'.");
            }

            return normalized;
        }

        private static Dictionary<string, string> CreateBuildAdditionalData(
            ExportIdentity identity,
            DependencyResolution resolution)
        {
            var data = new SortedDictionary<string, string>(identity.Build.AdditionalData, StringComparer.Ordinal);
            AddRange(data, identity.AdditionalData);
            foreach (var laneData in identity.Lane.AdditionalData)
            {
                data[$"lane.{laneData.Key}"] = laneData.Value;
            }

            data["aspnetCoreGitHash"] = resolution.AspNetCore!.CommitHash!;
            data["aspnetCoreVersion"] = resolution.AspNetCore.Version ?? string.Empty;
            data["azureDevOpsBuildId"] = identity.AzureDevOps.BuildId;
            data["azureDevOpsBuildNumber"] = identity.AzureDevOps.BuildNumber;
            data["azureDevOpsBuildUrl"] = identity.AzureDevOps.BuildUrl;
            data["azureDevOpsPipeline"] = identity.AzureDevOps.Pipeline;
            data["azureDevOpsProject"] = identity.AzureDevOps.Project;
            data["benchmarksGitHash"] = identity.PerfRepoHash;
            data["crankVersion"] = identity.CrankVersion;
            data["dependencies"] = SerializeDependencies(resolution.Dependencies);
            data["laneName"] = identity.Lane.Name;
            data["productVersion"] = resolution.Runtime.Version ?? identity.Build.Version ?? string.Empty;
            data["runtimeArtifactId"] = identity.Build.ArtifactId ?? string.Empty;
            data["runtimeVersion"] = resolution.Runtime.Version ?? identity.Build.Version ?? string.Empty;
            return new Dictionary<string, string>(data, StringComparer.Ordinal);
        }

        private static Dictionary<string, string> CreateTestAdditionalData(
            CrankExecutionResult execution,
            ExportIdentity identity,
            ExportSourceMetadata source,
            IReadOnlyList<(PerfLabCounter Counter, string SourcePath, bool IsMapped)> counters)
        {
            var data = new SortedDictionary<string, string>(identity.Scenario.AdditionalData, StringComparer.Ordinal)
            {
                ["crank.counterPolicyPath"] = source.CounterPolicyPath,
                ["crank.counterSources"] = JsonSerializer.Serialize(
                    counters.Select(counter => new
                    {
                        name = counter.Counter.Name,
                        path = counter.SourcePath
                    }),
                    ContractJson.CreateSerializerOptions()),
                ["crank.exportIdentitySource"] = source.ExportIdentitySource,
                ["crank.independentSampleCount"] = "1",
                ["crank.measurementPointCount"] = CountMeasurements(execution).ToString(CultureInfo.InvariantCulture),
                ["crank.measurementsUsedAsSamples"] = "false",
                ["crank.resultPath"] = source.CrankResultPath,
                ["crank.sampleModel"] = SampleModel,
                ["crank.sqlSession"] = identity.Sql.Session,
                ["crank.unmappedCounterPaths"] = JsonSerializer.Serialize(
                    counters
                        .Where(counter => !counter.IsMapped)
                        .Select(counter => counter.SourcePath)
                        .OrderBy(path => path, StringComparer.Ordinal),
                    ContractJson.CreateSerializerOptions())
            };

            if (!string.IsNullOrWhiteSpace(identity.Sql.Table))
            {
                data["crank.sqlTable"] = identity.Sql.Table;
            }

            if (!string.IsNullOrWhiteSpace(identity.Sql.RecordId))
            {
                data["crank.sqlRecordId"] = identity.Sql.RecordId;
            }

            if (!source.ExportIdentitySource.StartsWith(
                    "crank-properties:",
                    StringComparison.Ordinal))
            {
                data["crank.exportIdentityPath"] = source.ExportIdentitySource;
            }

            foreach (var property in execution.JobResults.Properties.OrderBy(property => property.Key, StringComparer.Ordinal))
            {
                data[$"crank.property.{property.Key}"] = property.Value;
            }

            return new Dictionary<string, string>(data, StringComparer.Ordinal);
        }

        private static string SerializeDependencies(IReadOnlyList<ResolvedDependency> dependencies)
        {
            var payload = dependencies.Select(dependency => new
            {
                name = dependency.Name,
                aliases = dependency.Aliases,
                repository = dependency.Repository,
                branch = dependency.Branch,
                version = dependency.Version,
                commitHash = dependency.CommitHash,
                commitTimeStamp = dependency.CommitTimeStamp?.ToUniversalTime(),
                sourceJobs = dependency.SourceJobs,
                additionalData = dependency.AdditionalData
            });
            return JsonSerializer.Serialize(payload, ContractJson.CreateSerializerOptions());
        }

        private static int CountMeasurements(CrankExecutionResult execution)
        {
            return execution.JobResults.Jobs.Values.Sum(job =>
                job.Measurements.Sum(series => series.Count));
        }

        private static string? NormalizeCorrelationId(string? correlationId)
        {
            return string.IsNullOrWhiteSpace(correlationId)
                ? null
                : Guid.Parse(correlationId).ToString("D");
        }

        private static Dictionary<string, string> ToSortedDictionary(
            IReadOnlyDictionary<string, string> values)
        {
            return values
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        }

        private static void AddRange(
            IDictionary<string, string> destination,
            IReadOnlyDictionary<string, string> source)
        {
            foreach (var pair in source)
            {
                destination[pair.Key] = pair.Value;
            }
        }
    }
}
