// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

namespace Crank.PerfLabExporter.Conversion
{
    internal static class RepositoryIdentity
    {
        private const string GitHubPrefix = "github.com/";

        public static string NormalizeRepository(string? repository)
        {
            if (string.IsNullOrWhiteSpace(repository))
            {
                return string.Empty;
            }

            var value = repository.Trim().Replace('\\', '/');
            if (value.StartsWith("git@", StringComparison.OrdinalIgnoreCase))
            {
                value = value["git@".Length..].Replace(':', '/');
            }
            else if (Uri.TryCreate(value, UriKind.Absolute, out var uri))
            {
                value = $"{uri.Host}{uri.AbsolutePath}";
            }

            value = value.Trim('/');
            if (value.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
            {
                value = value[..^".git".Length];
            }

            if (value.Count(character => character == '/') == 1)
            {
                value = GitHubPrefix + value;
            }

            return value.ToLowerInvariant();
        }

        public static string NormalizePackage(string? package)
        {
            return string.IsNullOrWhiteSpace(package)
                ? string.Empty
                : package.Trim().ToLowerInvariant();
        }

        public static bool IsRuntimeRepository(string? repository)
        {
            return NormalizeRepository(repository) == "github.com/dotnet/runtime";
        }

        public static bool IsAspNetCoreRepository(string? repository)
        {
            return NormalizeRepository(repository) == "github.com/dotnet/aspnetcore";
        }

        public static bool IsRuntimePackage(string? package)
        {
            var normalized = NormalizePackage(package);
            return normalized is "microsoft.netcore.app" or "microsoft.netcore.app.ref" ||
                normalized.StartsWith("microsoft.netcore.app.runtime.", StringComparison.Ordinal);
        }

        public static bool IsAspNetCorePackage(string? package)
        {
            var normalized = NormalizePackage(package);
            return normalized is "microsoft.aspnetcore.app" or "microsoft.aspnetcore.app.ref" ||
                normalized.StartsWith("microsoft.aspnetcore.app.runtime.", StringComparison.Ordinal);
        }

        public static string ToCanonicalUrl(string repository)
        {
            var normalized = NormalizeRepository(repository);
            return normalized.StartsWith(GitHubPrefix, StringComparison.Ordinal)
                ? $"https://{normalized}"
                : repository.Trim().TrimEnd('/');
        }

        public static bool TryGetGitHubRepository(string repository, out string owner, out string name)
        {
            owner = string.Empty;
            name = string.Empty;
            var normalized = NormalizeRepository(repository);
            if (!normalized.StartsWith(GitHubPrefix, StringComparison.Ordinal))
            {
                return false;
            }

            var segments = normalized[GitHubPrefix.Length..].Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length != 2)
            {
                return false;
            }

            owner = segments[0];
            name = segments[1];
            return true;
        }
    }
}
