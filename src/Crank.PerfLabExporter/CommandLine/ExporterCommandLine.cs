// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Globalization;
using Crank.PerfLabExporter.Publishing;

namespace Crank.PerfLabExporter.CommandLine
{
    internal static class ExporterCommandLine
    {
        public const string Help =
            """
            Converts Crank --json output to PerfLab JSON and optionally uploads it.

            Usage:
              Crank.PerfLabExporter convert [options]
              Crank.PerfLabExporter upload  [options]

            Required:
              --crank-json <path>
              --counter-policy <path>

            Conversion:
              --identity-property-prefix <prefix>         Default: perflab.
              --crank-version-environment-variable <name>
              --output-directory <path>
              --github-token-environment-variable <name> Default: GITHUB_TOKEN

            Upload:
              --storage-account <name-or-uri>
              --container <name>
              --queue <name>
              --storage-authentication <default|managed-identity|certificate>
              --managed-identity-client-id[-environment-variable] <value>
              --tenant-id[-environment-variable] <value>
              --client-id[-environment-variable] <value>
              --certificate-path[-environment-variable] <value>
              --certificate-base64-environment-variable <name>
              --certificate-password-environment-variable <name>
              --maximum-attempts <count>
              --retry-delay-seconds <seconds>
            """;

        public static ExporterOptions Parse(string[] args)
        {
            if (args.Length == 0 || args[0] is "-h" or "--help")
            {
                return new ExporterOptions { ShowHelp = true };
            }

            var mode = args[0] switch
            {
                "convert" => ExportMode.Convert,
                "upload" => ExportMode.Upload,
                _ => throw new ArgumentException(
                    $"Unknown command '{args[0]}'.")
            };
            var values = ParseOptions(args[1..]);
            if (values.Remove("--help") || values.Remove("-h"))
            {
                return new ExporterOptions
                {
                    Mode = mode,
                    ShowHelp = true
                };
            }

            var identitySource = Take(values, "--identity-source");
            if (identitySource is not null &&
                !identitySource.Equals(
                    "crank",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    "Only '--identity-source crank' is supported.");
            }

            var storage = mode == ExportMode.Upload
                ? ParseStorage(values)
                : (
                    Account: (string?)null,
                    Container: (string?)null,
                    Queue: (string?)null,
                    Authentication: new StorageAuthenticationOptions(),
                    Retry: PublicationRetryOptions.Default);
            var options = new ExporterOptions
            {
                Mode = mode,
                CrankJsonPath = Required(values, "--crank-json"),
                CounterPolicyPath = Required(values, "--counter-policy"),
                LiveIdentity = new LiveIdentityOptions
                {
                    PropertyPrefix =
                        Take(values, "--identity-property-prefix") ??
                        "perflab.",
                    CrankVersionEnvironmentVariable = Take(
                        values,
                        "--crank-version-environment-variable")
                },
                OutputDirectory =
                    Take(values, "--output-directory") ??
                    Environment.CurrentDirectory,
                GitHubTokenEnvironmentVariable =
                    Take(
                        values,
                        "--github-token-environment-variable") ??
                    "GITHUB_TOKEN",
                StorageAccount = storage.Account,
                Container = storage.Container,
                Queue = storage.Queue,
                Authentication = storage.Authentication,
                Retry = storage.Retry
            };

            if (values.Count > 0)
            {
                throw new ArgumentException(
                    $"Unknown option '{values.Keys.Order().First()}'.");
            }

            return options;
        }

        private static (
            string Account,
            string Container,
            string Queue,
            StorageAuthenticationOptions Authentication,
            PublicationRetryOptions Retry) ParseStorage(
                IDictionary<string, string?> values)
        {
            var authentication = new StorageAuthenticationOptions
            {
                Mode = Take(values, "--storage-authentication")
                    ?.ToLowerInvariant() switch
                    {
                        null or "default" =>
                            StorageAuthenticationMode.Default,
                        "managed-identity" =>
                            StorageAuthenticationMode.ManagedIdentity,
                        "certificate" =>
                            StorageAuthenticationMode.Certificate,
                        var value => throw new ArgumentException(
                            $"Unknown storage authentication mode '{value}'.")
                    },
                ManagedIdentityClientId =
                    Take(values, "--managed-identity-client-id"),
                ManagedIdentityClientIdEnvironmentVariable = Take(
                    values,
                    "--managed-identity-client-id-environment-variable"),
                TenantId = Take(values, "--tenant-id"),
                TenantIdEnvironmentVariable =
                    Take(values, "--tenant-id-environment-variable"),
                ClientId = Take(values, "--client-id"),
                ClientIdEnvironmentVariable =
                    Take(values, "--client-id-environment-variable"),
                CertificatePath = Take(values, "--certificate-path"),
                CertificatePathEnvironmentVariable = Take(
                    values,
                    "--certificate-path-environment-variable"),
                CertificateBase64EnvironmentVariable = Take(
                    values,
                    "--certificate-base64-environment-variable"),
                CertificatePasswordEnvironmentVariable = Take(
                    values,
                    "--certificate-password-environment-variable")
            };
            var maximumAttempts = ParsePositiveInteger(
                Take(values, "--maximum-attempts"),
                3,
                "--maximum-attempts");
            var delaySeconds = ParseNonNegativeDouble(
                Take(values, "--retry-delay-seconds"),
                2,
                "--retry-delay-seconds");
            return (
                Required(values, "--storage-account"),
                Required(values, "--container"),
                Required(values, "--queue"),
                authentication,
                new PublicationRetryOptions(
                    maximumAttempts,
                    TimeSpan.FromSeconds(delaySeconds)));
        }

        private static Dictionary<string, string?> ParseOptions(string[] args)
        {
            var values = new Dictionary<string, string?>(StringComparer.Ordinal);
            for (var index = 0; index < args.Length; index++)
            {
                var option = args[index];
                if (option is "-h" or "--help")
                {
                    values.Add(option, null);
                    continue;
                }

                if (!option.StartsWith("--", StringComparison.Ordinal) ||
                    index + 1 >= args.Length ||
                    args[index + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    throw new ArgumentException(
                        $"Option '{option}' requires a value.");
                }

                if (!values.TryAdd(option, args[++index]))
                {
                    throw new ArgumentException(
                        $"Option '{option}' was supplied more than once.");
                }
            }

            return values;
        }

        private static string Required(
            IDictionary<string, string?> values,
            string option)
        {
            return Take(values, option) is { Length: > 0 } value
                ? value
                : throw new ArgumentException(
                    $"Required option '{option}' was not supplied.");
        }

        private static string? Take(
            IDictionary<string, string?> values,
            string option)
        {
            return values.Remove(option, out var value) ? value : null;
        }

        private static int ParsePositiveInteger(
            string? value,
            int defaultValue,
            string option)
        {
            if (value is null)
            {
                return defaultValue;
            }

            return int.TryParse(
                value,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var parsed) && parsed > 0
                    ? parsed
                    : throw new ArgumentException(
                        $"Option '{option}' must be a positive integer.");
        }

        private static double ParseNonNegativeDouble(
            string? value,
            double defaultValue,
            string option)
        {
            if (value is null)
            {
                return defaultValue;
            }

            return double.TryParse(
                value,
                NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture,
                out var parsed) &&
                double.IsFinite(parsed) &&
                parsed >= 0
                    ? parsed
                    : throw new ArgumentException(
                        $"Option '{option}' must be non-negative.");
        }
    }
}
