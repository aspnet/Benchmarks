// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Crank.PerfLabExporter.CommandLine;
using Crank.PerfLabExporter.Publishing;

namespace Crank.PerfLabExporter.Tests
{
    public class ExporterCommandLineTests
    {
        [Fact]
        public void ParsesPipelineUploadArguments()
        {
            var options = ExporterCommandLine.Parse(
            [
                "upload",
                "--crank-json", "crank.json",
                "--counter-policy", "policy.json",
                "--identity-source", "crank",
                "--identity-property-prefix", "perflab.",
                "--crank-version-environment-variable", "CRANK_VERSION",
                "--storage-account", "account",
                "--container", "results",
                "--queue", "resultsqueue",
                "--storage-authentication", "certificate",
                "--tenant-id-environment-variable", "TENANT_ID",
                "--client-id-environment-variable", "CLIENT_ID",
                "--certificate-base64-environment-variable", "CERTIFICATE",
                "--certificate-password-environment-variable",
                "CERTIFICATE_PASSWORD"
            ]);

            Assert.Equal(ExportMode.Upload, options.Mode);
            Assert.Equal("perflab.", options.LiveIdentity.PropertyPrefix);
            Assert.Equal(
                "CRANK_VERSION",
                options.LiveIdentity.CrankVersionEnvironmentVariable);
            Assert.Equal(
                StorageAuthenticationMode.Certificate,
                options.Authentication.Mode);
        }

        [Fact]
        public void RejectsUnknownOptions()
        {
            var exception = Assert.Throws<ArgumentException>(() =>
                ExporterCommandLine.Parse(
                [
                    "convert",
                    "--crank-json", "crank.json",
                    "--counter-policy", "policy.json",
                    "--unknown", "value"
                ]));

            Assert.Contains("--unknown", exception.Message);
        }
    }
}
