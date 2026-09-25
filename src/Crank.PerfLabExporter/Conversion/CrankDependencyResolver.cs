// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Crank.PerfLabExporter.Contracts.Crank;
using Crank.PerfLabExporter.Contracts.Identity;

namespace Crank.PerfLabExporter.Conversion
{
    public sealed class ResolvedDependency
    {
        public string Name { get; init; } = string.Empty;

        public IReadOnlyList<string> Aliases { get; init; } = [];

        public string Repository { get; init; } = string.Empty;

        public string? Branch { get; init; }

        public string? Version { get; init; }

        public string? CommitHash { get; init; }

        public DateTimeOffset? CommitTimeStamp { get; init; }

        public IReadOnlyList<string> SourceJobs { get; init; } = [];

        public IReadOnlyDictionary<string, string> AdditionalData { get; init; } =
            new Dictionary<string, string>();
    }

    public sealed record DependencyResolution(
        ResolvedDependency Runtime,
        ResolvedDependency? AspNetCore,
        IReadOnlyList<ResolvedDependency> Dependencies);

    public static class CrankDependencyResolver
    {
        public static DependencyResolution Resolve(
            CrankExecutionResult execution,
            ExportIdentity identity)
        {
            var observations = execution.JobResults.Jobs
                .OrderBy(job => job.Key, StringComparer.Ordinal)
                .SelectMany(job => job.Value.Dependencies.Select(dependency => new DependencyObservation(job.Key, dependency)))
                .ToList();

            var runtimeObservations = SelectRoleObservations(
                observations,
                observation => GetAliases(observation).Any(RepositoryIdentity.IsRuntimePackage),
                observation => RepositoryIdentity.IsRuntimeRepository(observation.Dependency.RepositoryUrl));
            if (runtimeObservations.Count == 0)
            {
                throw new CrankConversionException(
                    "The Crank result does not contain a Microsoft.NETCore.App/dotnet/runtime dependency. " +
                    "Runtime identity is resolved by normalized package/repository identity, not dependency array position.");
            }

            var aspNetCoreObservations = SelectRoleObservations(
                observations,
                observation => GetAliases(observation).Any(RepositoryIdentity.IsAspNetCorePackage),
                observation => RepositoryIdentity.IsAspNetCoreRepository(observation.Dependency.RepositoryUrl));
            if (aspNetCoreObservations.Count == 0)
            {
                throw new CrankConversionException(
                    "The Crank result does not contain a Microsoft.AspNetCore.App/dotnet/aspnetcore dependency.");
            }

            var runtime = ResolveRole("runtime", runtimeObservations, identity.Dependencies, requiredCommit: true);
            if (string.IsNullOrWhiteSpace(runtime.Repository))
            {
                runtime = CopyWithRepository(runtime, identity.Build.Repo);
            }

            var aspNetCore = ResolveRole(
                "aspnetcore",
                aspNetCoreObservations,
                identity.Dependencies,
                requiredCommit: true);

            ValidatePrimaryBuild(identity.Build, runtime);

            var roleObservations = new HashSet<DependencyObservation>(runtimeObservations);
            roleObservations.UnionWith(aspNetCoreObservations);

            var dependencies = new List<ResolvedDependency> { runtime, aspNetCore };

            foreach (var group in observations
                .Where(observation => !roleObservations.Contains(observation))
                .GroupBy(CreateObservationKey, StringComparer.Ordinal)
                .OrderBy(group => group.Key, StringComparer.Ordinal))
            {
                dependencies.Add(ResolveRole("dependency", group.ToList(), identity.Dependencies, requiredCommit: false));
            }

            foreach (var metadata in identity.Dependencies)
            {
                if (dependencies.Any(dependency => Matches(metadata, dependency)))
                {
                    continue;
                }

                dependencies.Add(FromMetadata(metadata));
            }

            return new DependencyResolution(
                runtime,
                aspNetCore,
                dependencies
                    .OrderBy(dependency => dependency.Name, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(dependency => dependency.Repository, StringComparer.OrdinalIgnoreCase)
                    .ToList());
        }

        private static ResolvedDependency CopyWithRepository(
            ResolvedDependency dependency,
            string repository)
        {
            return new ResolvedDependency
            {
                Name = dependency.Name,
                Aliases = dependency.Aliases,
                Repository = RepositoryIdentity.ToCanonicalUrl(repository),
                Branch = dependency.Branch,
                Version = dependency.Version,
                CommitHash = dependency.CommitHash,
                CommitTimeStamp = dependency.CommitTimeStamp,
                SourceJobs = dependency.SourceJobs,
                AdditionalData = dependency.AdditionalData
            };
        }

        private static List<DependencyObservation> SelectRoleObservations(
            IReadOnlyList<DependencyObservation> observations,
            Func<DependencyObservation, bool> packageMatch,
            Func<DependencyObservation, bool> repositoryMatch)
        {
            var packageMatches = observations.Where(packageMatch).ToList();
            return packageMatches.Count > 0
                ? packageMatches
                : observations.Where(repositoryMatch).ToList();
        }

        private static ResolvedDependency ResolveRole(
            string role,
            IReadOnlyList<DependencyObservation> observations,
            IReadOnlyList<DependencyMetadata> metadata,
            bool requiredCommit)
        {
            var aliases = observations
                .SelectMany(GetAliases)
                .Where(alias => !string.IsNullOrWhiteSpace(alias))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(alias => alias, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var repositories = DistinctNonEmpty(observations.Select(observation => observation.Dependency.RepositoryUrl), RepositoryIdentity.NormalizeRepository);
            var versions = DistinctNonEmpty(observations.Select(observation => observation.Dependency.Version), value => value.Trim());
            var commits = DistinctNonEmpty(observations.Select(observation => observation.Dependency.CommitHash), value => value.Trim().ToLowerInvariant());

            if (repositories.Count > 1)
            {
                throw new CrankConversionException(
                    $"Conflicting {role} repositories were found: {string.Join(", ", repositories)}.");
            }

            if (versions.Count > 1)
            {
                throw new CrankConversionException(
                    $"Conflicting {role} versions were found: {string.Join(", ", versions)}.");
            }

            if (commits.Count > 1)
            {
                throw new CrankConversionException(
                    $"Conflicting {role} commit hashes were found: {string.Join(", ", commits)}.");
            }

            var matchingMetadata = metadata.Where(candidate =>
                Matches(candidate, aliases, repositories.SingleOrDefault())).ToList();
            if (matchingMetadata.Count > 1)
            {
                throw new CrankConversionException(
                    $"More than one export identity dependency matches the normalized {role} identity.");
            }

            var enrichment = matchingMetadata.SingleOrDefault();
            var repository = repositories.SingleOrDefault() ?? enrichment?.Repository ?? string.Empty;
            var version = versions.SingleOrDefault() ?? enrichment?.Version;
            var commit = commits.SingleOrDefault() ?? enrichment?.CommitHash;
            ValidateEnrichment(role, repository, version, commit, enrichment);

            if (requiredCommit && string.IsNullOrWhiteSpace(commit))
            {
                throw new CrankConversionException($"The resolved {role} dependency does not contain a commit hash.");
            }

            var name = enrichment?.Name;
            if (string.IsNullOrWhiteSpace(name))
            {
                name = role switch
                {
                    "runtime" => "runtime",
                    "aspnetcore" => "aspnetcore",
                    _ => aliases.FirstOrDefault() ?? repository
                };
            }

            return new ResolvedDependency
            {
                Name = name,
                Aliases = aliases
                    .Concat(enrichment?.Aliases ?? [])
                    .Where(alias => !string.IsNullOrWhiteSpace(alias))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(alias => alias, StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                Repository = string.IsNullOrWhiteSpace(repository)
                    ? string.Empty
                    : RepositoryIdentity.ToCanonicalUrl(repository),
                Branch = enrichment?.Branch,
                Version = version,
                CommitHash = commit,
                CommitTimeStamp = enrichment?.CommitTimeStamp,
                SourceJobs = observations
                    .Select(observation => observation.Job)
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(job => job, StringComparer.Ordinal)
                    .ToList(),
                AdditionalData = new SortedDictionary<string, string>(
                    enrichment?.AdditionalData ?? [],
                    StringComparer.Ordinal)
            };
        }

        private static ResolvedDependency FromMetadata(DependencyMetadata metadata)
        {
            return new ResolvedDependency
            {
                Name = metadata.Name,
                Aliases = metadata.Aliases
                    .Where(alias => !string.IsNullOrWhiteSpace(alias))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(alias => alias, StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                Repository = RepositoryIdentity.ToCanonicalUrl(metadata.Repository),
                Branch = metadata.Branch,
                Version = metadata.Version,
                CommitHash = metadata.CommitHash,
                CommitTimeStamp = metadata.CommitTimeStamp,
                AdditionalData = new SortedDictionary<string, string>(metadata.AdditionalData, StringComparer.Ordinal)
            };
        }

        private static void ValidatePrimaryBuild(
            PrimaryBuildIdentity build,
            ResolvedDependency runtime)
        {
            var buildRepository = RepositoryIdentity.NormalizeRepository(build.Repo);
            var runtimeRepository = RepositoryIdentity.NormalizeRepository(runtime.Repository);
            if (!RepositoryIdentity.IsRuntimeRepository(build.Repo) ||
                (!string.IsNullOrWhiteSpace(runtimeRepository) &&
                 !string.Equals(buildRepository, runtimeRepository, StringComparison.Ordinal)))
            {
                throw new CrankConversionException(
                    $"Primary build repository '{build.Repo}' does not match the resolved dotnet/runtime dependency.");
            }

            if (!string.Equals(build.GitHash, runtime.CommitHash, StringComparison.OrdinalIgnoreCase))
            {
                throw new CrankConversionException(
                    $"Primary build commit '{build.GitHash}' does not match resolved runtime commit '{runtime.CommitHash}'.");
            }

            if (!string.IsNullOrWhiteSpace(build.Version) &&
                !string.IsNullOrWhiteSpace(runtime.Version) &&
                !string.Equals(build.Version, runtime.Version, StringComparison.OrdinalIgnoreCase))
            {
                throw new CrankConversionException(
                    $"Primary build version '{build.Version}' does not match resolved runtime version '{runtime.Version}'.");
            }

            if (!string.IsNullOrWhiteSpace(runtime.Branch) &&
                !string.Equals(build.Branch, runtime.Branch, StringComparison.OrdinalIgnoreCase))
            {
                throw new CrankConversionException(
                    $"Primary build branch '{build.Branch}' does not match resolved runtime branch '{runtime.Branch}'.");
            }
        }

        private static void ValidateEnrichment(
            string role,
            string repository,
            string? version,
            string? commit,
            DependencyMetadata? enrichment)
        {
            if (enrichment is null)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(repository) &&
                !string.Equals(
                    RepositoryIdentity.NormalizeRepository(repository),
                    RepositoryIdentity.NormalizeRepository(enrichment.Repository),
                    StringComparison.Ordinal))
            {
                throw new CrankConversionException($"Export identity metadata contradicts the Crank {role} repository.");
            }

            if (!string.IsNullOrWhiteSpace(version) &&
                !string.IsNullOrWhiteSpace(enrichment.Version) &&
                !string.Equals(version, enrichment.Version, StringComparison.OrdinalIgnoreCase))
            {
                throw new CrankConversionException($"Export identity metadata contradicts the Crank {role} version.");
            }

            if (!string.IsNullOrWhiteSpace(commit) &&
                !string.IsNullOrWhiteSpace(enrichment.CommitHash) &&
                !string.Equals(commit, enrichment.CommitHash, StringComparison.OrdinalIgnoreCase))
            {
                throw new CrankConversionException($"Export identity metadata contradicts the Crank {role} commit.");
            }
        }

        private static IEnumerable<string> GetAliases(DependencyObservation observation)
        {
            if (!string.IsNullOrWhiteSpace(observation.Dependency.Id))
            {
                yield return observation.Dependency.Id;
            }

            foreach (var name in observation.Dependency.Names)
            {
                yield return name;
            }
        }

        private static string CreateObservationKey(DependencyObservation observation)
        {
            var repository = RepositoryIdentity.NormalizeRepository(observation.Dependency.RepositoryUrl);
            var commit = observation.Dependency.CommitHash?.Trim().ToLowerInvariant() ?? string.Empty;
            var alias = GetAliases(observation)
                .Select(RepositoryIdentity.NormalizePackage)
                .FirstOrDefault(value => value.Length > 0) ?? string.Empty;
            return $"{repository}|{commit}|{alias}";
        }

        private static bool Matches(DependencyMetadata metadata, ResolvedDependency dependency)
        {
            return Matches(metadata, dependency.Aliases, dependency.Repository);
        }

        private static bool Matches(
            DependencyMetadata metadata,
            IReadOnlyCollection<string> aliases,
            string? repository)
        {
            var normalizedAliases = aliases
                .Select(RepositoryIdentity.NormalizePackage)
                .Where(alias => alias.Length > 0)
                .ToHashSet(StringComparer.Ordinal);
            var metadataAliases = metadata.Aliases
                .Append(metadata.Name)
                .Select(RepositoryIdentity.NormalizePackage)
                .Where(alias => alias.Length > 0)
                .ToHashSet(StringComparer.Ordinal);
            if (metadataAliases.Overlaps(normalizedAliases))
            {
                return true;
            }

            if (metadata.Aliases.Count > 0 && normalizedAliases.Count > 0)
            {
                return false;
            }

            var metadataRepository = RepositoryIdentity.NormalizeRepository(metadata.Repository);
            var dependencyRepository = RepositoryIdentity.NormalizeRepository(repository);
            return metadataRepository.Length > 0 &&
                dependencyRepository.Length > 0 &&
                string.Equals(metadataRepository, dependencyRepository, StringComparison.Ordinal);
        }

        private static List<string> DistinctNonEmpty(
            IEnumerable<string?> values,
            Func<string, string> normalize)
        {
            return values
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => normalize(value!))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private sealed record DependencyObservation(string Job, CrankDependency Dependency);
    }
}
