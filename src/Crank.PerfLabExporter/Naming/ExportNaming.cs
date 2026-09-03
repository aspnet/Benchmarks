// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Security.Cryptography;
using System.Text;
using Crank.PerfLabExporter.Contracts.Identity;
using Crank.PerfLabExporter.Contracts.PerfLab;

namespace Crank.PerfLabExporter.Naming
{
    public sealed record ExportNames(string FileName, string BlobName);

    public static class ExportNaming
    {
        public static ExportNames Create(PerfLabReport report, ExportIdentity identity)
        {
            if (!string.IsNullOrWhiteSpace(identity.Sql.Table) &&
                !string.IsNullOrWhiteSpace(identity.Sql.RecordId))
            {
                return CreateSqlRowNames(identity.Sql.Table, identity.Sql.RecordId);
            }

            var test = report.Tests.Single();
            var canonicalIdentity = string.Join(
                "\n",
                report.Build.Repo,
                report.Build.Branch,
                report.Build.GitHash,
                report.Build.BuildName,
                report.Build.TimeStamp.ToUniversalTime().ToString("O"),
                report.Build.Architecture,
                report.Build.Locale,
                report.Os.Name,
                report.Os.Architecture,
                report.Os.Locale,
                report.Os.MachineName,
                report.Run.Hidden.ToString(),
                report.Run.CorrelationId,
                report.Run.PerfRepoHash,
                report.Run.Name,
                report.Run.Queue,
                string.Join(
                    ";",
                    report.Run.Configurations
                        .OrderBy(configuration => configuration.Key, StringComparer.Ordinal)
                        .Select(configuration => $"{configuration.Key}={configuration.Value}")),
                test.Name,
                string.Join(
                    ";",
                    identity.Scenario.AdditionalData
                        .OrderBy(data => data.Key, StringComparer.Ordinal)
                        .Select(data => $"{data.Key}={data.Value}")),
                identity.Sql.Session,
                identity.Sql.Table,
                identity.Sql.RecordId);
            var identityHash = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(canonicalIdentity)))[..16].ToLowerInvariant();
            var runtimeHash = Slug(report.Build.GitHash, 16);
            var family = Slug(report.Run.Name, 48);
            var scenario = Slug(test.Name, 64);
            var queue = Slug(report.Run.Queue, 64);
            var fileName = $"{family}-{scenario}-{runtimeHash}-{identityHash}.perflab.json";
            var blobName = $"crank/{family}/{queue}/{runtimeHash}/{fileName}";
            return new ExportNames(fileName, blobName);
        }

        private static ExportNames CreateSqlRowNames(string table, string recordId)
        {
            var canonicalIdentity = $"{table.Trim().ToLowerInvariant()}\n{recordId.Trim()}";
            var identityHash = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(canonicalIdentity)))[..16]
                .ToLowerInvariant();
            var tableSlug = Slug(table, 64);
            var recordSlug = Slug(recordId, 64);
            var fileName = $"{tableSlug}-{recordSlug}-{identityHash}.perflab.json";
            var blobName = $"crank/sql/{tableSlug}/{identityHash}/{fileName}";
            return new ExportNames(fileName, blobName);
        }

        private static string Slug(string value, int maximumLength)
        {
            var builder = new StringBuilder(value.Length);
            var previousSeparator = false;
            foreach (var character in value.Normalize(NormalizationForm.FormKC))
            {
                if (char.IsAsciiLetterOrDigit(character))
                {
                    builder.Append(char.ToLowerInvariant(character));
                    previousSeparator = false;
                }
                else if (!previousSeparator && builder.Length > 0)
                {
                    builder.Append('-');
                    previousSeparator = true;
                }

                if (builder.Length >= maximumLength)
                {
                    break;
                }
            }

            var result = builder.ToString().Trim('-');
            return result.Length == 0 ? "value" : result;
        }
    }
}
