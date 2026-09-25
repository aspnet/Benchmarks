// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Globalization;
using Crank.PerfLabExporter.CommandLine;
using Crank.PerfLabExporter.Contracts.Crank;
using Crank.PerfLabExporter.Contracts.Identity;
using Crank.PerfLabExporter.Contracts.PerfLab;

namespace Crank.PerfLabExporter.Conversion
{
    internal static class LiveExportIdentityBuilder
    {
        private static readonly string[] RequiredConfigurations =
        [
            "Framework",
            "Runtime",
            "Cores",
            "Topology"
        ];

        public static ExportIdentity Build(
            CrankExecutionResult execution,
            LiveIdentityOptions options)
        {
            var properties = new NamespacedProperties(
                execution.JobResults.Properties,
                options.PropertyPrefix);
            var runtime = ResolveDependency(
                execution,
                "runtime",
                RepositoryIdentity.IsRuntimePackage,
                RepositoryIdentity.IsRuntimeRepository);
            var aspNetCore = ResolveDependency(
                execution,
                "aspnetcore",
                RepositoryIdentity.IsAspNetCorePackage,
                RepositoryIdentity.IsAspNetCoreRepository);
            var configurations = properties.GetChildren("configuration.");
            foreach (var configuration in RequiredConfigurations)
            {
                _ = Required(
                    configurations.GetValueOrDefault(configuration),
                    $"configuration.{configuration}");
            }

            var crankVersion = execution.CrankVersion;
            if (string.IsNullOrWhiteSpace(crankVersion) &&
                !string.IsNullOrWhiteSpace(
                    options.CrankVersionEnvironmentVariable))
            {
                crankVersion = Environment.GetEnvironmentVariable(
                    options.CrankVersionEnvironmentVariable);
            }

            var runtimeRepository = Required(
                properties.Get("build.repo") ?? runtime.Repository,
                "build.repo");
            var runtimeBranch = Required(
                properties.Get("build.branch"),
                "build.branch");
            var runtimeCommit = Required(
                runtime.CommitHash,
                "runtime dependency commit");
            var runtimeBuildName = properties.Get("build.name") ??
                (!string.IsNullOrWhiteSpace(runtime.Version)
                    ? $"runtime-{runtime.Version}"
                    : $"runtime-{ShortHash(runtimeCommit)}");
            var runtimeTimestamp = ParseTimestamp(
                properties.Get("build.timeStamp"));

            return new ExportIdentity
            {
                Build = new PrimaryBuildIdentity
                {
                    Repo = runtimeRepository,
                    Branch = runtimeBranch,
                    GitHash = runtimeCommit,
                    BuildName = runtimeBuildName,
                    TimeStamp = runtimeTimestamp,
                    Version = runtime.Version,
                    ArtifactId = properties.Get("build.artifactId"),
                    AdditionalData =
                        properties.GetChildren("build.additionalData.")
                },
                Lane = new LaneIdentity
                {
                    Name = Required(properties.Get("lane.name"), "lane.name"),
                    Queue = Required(
                        properties.Get("lane.queue"),
                        "lane.queue"),
                    Os = new PerfLabOs
                    {
                        Name = Required(
                            properties.Get("lane.os.name"),
                            "lane.os.name"),
                        Architecture = Required(
                            properties.Get("lane.os.architecture"),
                            "lane.os.architecture"),
                        Locale = Required(
                            properties.Get("lane.os.locale"),
                            "lane.os.locale"),
                        MachineName = properties.Get("lane.os.machineName")
                    },
                    Configurations = configurations,
                    Hidden = bool.TryParse(
                        properties.Get("lane.hidden"),
                        out var hidden) && hidden,
                    AdditionalData =
                        properties.GetChildren("lane.additionalData.")
                },
                Scenario = new ScenarioIdentity
                {
                    Name = Required(
                        properties.Get("scenario.name"),
                        "scenario.name"),
                    Family = Required(
                        properties.Get("scenario.family"),
                        "scenario.family"),
                    Categories = ParseCategories(
                        properties.Get("scenario.categories")),
                    AdditionalData =
                        properties.GetChildren("scenario.additionalData.")
                },
                Dependencies =
                [
                    ToMetadata(
                        "runtime",
                        runtime,
                        runtimeBranch,
                        runtimeTimestamp),
                    ToMetadata(
                        "aspnetcore",
                        aspNetCore,
                        properties.Get("dependency.aspnetcore.branch"),
                        null)
                ],
                PerfRepoHash = Required(
                    properties.Get("perfRepoHash"),
                    "perfRepoHash"),
                CrankVersion = Required(crankVersion, "Crank version"),
                HelixCorrelationId = properties.Get("helixCorrelationId"),
                AzureDevOps = new AzureDevOpsMetadata
                {
                    Project = Required(
                        properties.Get("azureDevOps.project"),
                        "azureDevOps.project"),
                    Pipeline = Required(
                        properties.Get("azureDevOps.pipeline"),
                        "azureDevOps.pipeline"),
                    BuildId = Required(
                        properties.Get("azureDevOps.buildId"),
                        "azureDevOps.buildId"),
                    BuildNumber = Required(
                        properties.Get("azureDevOps.buildNumber"),
                        "azureDevOps.buildNumber"),
                    BuildUrl = Required(
                        properties.Get("azureDevOps.buildUrl"),
                        "azureDevOps.buildUrl")
                },
                Sql = new CrankSqlIdentity
                {
                    Session = Required(
                        properties.Get("sql.session"),
                        "sql.session"),
                    Table = properties.Get("sql.table"),
                    RecordId = properties.Get("sql.recordId")
                },
                AdditionalData = properties.GetChildren("additionalData.")
            };
        }

        private static DependencyFacts ResolveDependency(
            CrankExecutionResult execution,
            string name,
            Func<string?, bool> packageMatch,
            Func<string?, bool> repositoryMatch)
        {
            var dependencies = execution.JobResults.Jobs.Values
                .SelectMany(job => job.Dependencies)
                .ToList();
            var matches = dependencies
                .Where(dependency =>
                    dependency.Names.Append(dependency.Id).Any(packageMatch))
                .ToList();
            if (matches.Count == 0)
            {
                matches = dependencies
                    .Where(dependency =>
                        repositoryMatch(dependency.RepositoryUrl))
                    .ToList();
            }

            if (matches.Count == 0)
            {
                throw new CrankConversionException(
                    $"The Crank result does not contain a {name} dependency.");
            }

            var repository = matches
                .Select(dependency => dependency.RepositoryUrl)
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
            var commit = matches
                .Select(dependency => dependency.CommitHash)
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
            return new DependencyFacts(
                matches
                    .SelectMany(dependency =>
                        dependency.Names.Append(dependency.Id))
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                Required(repository, $"{name} dependency repository"),
                matches
                    .Select(dependency => dependency.Version)
                    .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)),
                Required(commit, $"{name} dependency commit"));
        }

        private static DependencyMetadata ToMetadata(
            string name,
            DependencyFacts dependency,
            string? branch,
            DateTimeOffset? timestamp)
        {
            return new DependencyMetadata
            {
                Name = name,
                Aliases = dependency.Aliases,
                Repository = RepositoryIdentity.ToCanonicalUrl(
                    dependency.Repository),
                Branch = branch,
                Version = dependency.Version,
                CommitHash = dependency.CommitHash,
                CommitTimeStamp = timestamp
            };
        }

        private static List<string> ParseCategories(string? value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? []
                : value
                    .Split([',', ';'], StringSplitOptions.RemoveEmptyEntries)
                    .Select(category => category.Trim())
                    .Where(category => category.Length > 0)
                    .Distinct(StringComparer.Ordinal)
                    .ToList();
        }

        private static DateTimeOffset? ParseTimestamp(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            if (DateTimeOffset.TryParse(
                    value,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal |
                    DateTimeStyles.AdjustToUniversal,
                    out var timestamp))
            {
                return timestamp;
            }

            throw new CrankConversionException(
                $"Crank property 'build.timeStamp' is not a valid timestamp.");
        }

        private static string Required(string? value, string name)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new CrankConversionException(
                    $"Live identity requires '{name}'.");
            }

            return value.Trim();
        }

        private static string ShortHash(string hash) =>
            hash.Length <= 12 ? hash : hash[..12];

        private sealed record DependencyFacts(
            List<string> Aliases,
            string Repository,
            string? Version,
            string CommitHash);

        private sealed class NamespacedProperties
        {
            private readonly Dictionary<string, string> _values =
                new(StringComparer.OrdinalIgnoreCase);

            public NamespacedProperties(
                IReadOnlyDictionary<string, string> properties,
                string prefix)
            {
                if (!prefix.EndsWith(".", StringComparison.Ordinal))
                {
                    prefix += ".";
                }

                foreach (var property in properties)
                {
                    if (property.Key.StartsWith(
                            prefix,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        _values[property.Key[prefix.Length..]] =
                            property.Value.Trim();
                    }
                }
            }

            public string? Get(string name) =>
                _values.GetValueOrDefault(name);

            public Dictionary<string, string> GetChildren(string prefix) =>
                _values
                    .Where(pair =>
                        pair.Key.StartsWith(
                            prefix,
                            StringComparison.OrdinalIgnoreCase) &&
                        pair.Key.Length > prefix.Length)
                    .ToDictionary(
                        pair => pair.Key[prefix.Length..],
                        pair => pair.Value,
                        StringComparer.Ordinal);
        }
    }
}
