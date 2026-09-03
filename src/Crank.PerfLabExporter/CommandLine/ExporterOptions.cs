// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Crank.PerfLabExporter.Publishing;

namespace Crank.PerfLabExporter.CommandLine
{
    internal enum ExportMode
    {
        Convert,
        Upload
    }

    internal sealed class LiveIdentityOptions
    {
        public string PropertyPrefix { get; init; } = "perflab.";
        public string? CrankVersionEnvironmentVariable { get; init; }
    }

    internal sealed class ExporterOptions
    {
        public ExportMode Mode { get; init; }
        public bool ShowHelp { get; init; }
        public string CrankJsonPath { get; init; } = string.Empty;
        public string CounterPolicyPath { get; init; } = string.Empty;
        public LiveIdentityOptions LiveIdentity { get; init; } = new();
        public string OutputDirectory { get; init; } =
            Environment.CurrentDirectory;
        public string GitHubTokenEnvironmentVariable { get; init; } =
            "GITHUB_TOKEN";
        public string? StorageAccount { get; init; }
        public string? Container { get; init; }
        public string? Queue { get; init; }
        public StorageAuthenticationOptions Authentication { get; init; } =
            new();
        public PublicationRetryOptions Retry { get; init; } =
            PublicationRetryOptions.Default;
    }
}
