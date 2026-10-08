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
            Assert.Null(options.StorageConnectionStringEnvironmentVariable);
        }

        [Fact]
        public void ParsesConnectionStringUploadWithoutAccount()
        {
            var options = ExporterCommandLine.Parse(
            [
                "upload",
                "--crank-json", "crank.json",
                "--counter-policy", "policy.json",
                "--storage-connection-string-environment-variable", "LOCAL_STORAGE",
                "--container", "results",
                "--queue", "resultsqueue"
            ]);

            Assert.Null(options.StorageAccount);
            Assert.Equal("LOCAL_STORAGE", options.StorageConnectionStringEnvironmentVariable);
        }

        [Theory]
        [InlineData("--storage-account", "account", "mutually exclusive")]
        [InlineData("--storage-authentication", "default", "Credential options")]
        [InlineData("--tenant-id-environment-variable", "TENANT", "Credential options")]
        [InlineData("--certificate-password-environment-variable", "PASSWORD", "Credential options")]
        public void RejectsConnectionStringWithAccountOrCredentials(
            string option, string value, string message)
        {
            var exception = Assert.Throws<ArgumentException>(() =>
                ExporterCommandLine.Parse(
                [
                    "upload",
                    "--crank-json", "crank.json",
                    "--counter-policy", "policy.json",
                    "--storage-connection-string-environment-variable", "LOCAL_STORAGE",
                    "--container", "results",
                    "--queue", "resultsqueue",
                    option, value
                ]));

            Assert.Contains(message, exception.Message);
        }

        [Theory]
        [InlineData("--container")]
        [InlineData("--queue")]
        public void ConnectionStringUploadStillRequiresContainerAndQueue(string missing)
        {
            var args = new List<string>
            {
                "upload",
                "--crank-json", "crank.json",
                "--counter-policy", "policy.json",
                "--storage-connection-string-environment-variable", "LOCAL_STORAGE"
            };
            foreach (var option in new[] { "--container", "--queue" })
            {
                if (option != missing)
                {
                    args.AddRange([option, "results"]);
                }
            }

            var exception = Assert.Throws<ArgumentException>(() =>
                ExporterCommandLine.Parse(args.ToArray()));
            Assert.Contains(missing, exception.Message);
        }

        [Fact]
        public void UploadRequiresAccountOrConnectionStringVariable()
        {
            var exception = Assert.Throws<ArgumentException>(() =>
                ExporterCommandLine.Parse(
                [
                    "upload",
                    "--crank-json", "crank.json",
                    "--counter-policy", "policy.json",
                    "--container", "results",
                    "--queue", "resultsqueue"
                ]));
            Assert.Contains("--storage-account", exception.Message);
            Assert.Contains("--storage-connection-string-environment-variable", exception.Message);
        }

        [Theory]
        [InlineData("")]
        [InlineData(" ")]
        public void RejectsEmptyConnectionStringVariableName(string name)
        {
            Assert.Throws<ArgumentException>(() => ExporterCommandLine.Parse(
            [
                "upload",
                "--crank-json", "crank.json",
                "--counter-policy", "policy.json",
                "--storage-connection-string-environment-variable", name,
                "--container", "results",
                "--queue", "resultsqueue"
            ]));
        }

        [Fact]
        public void ConnectionStringOptionRequiresAValue()
        {
            var exception = Assert.Throws<ArgumentException>(() =>
                ExporterCommandLine.Parse(
                [
                    "upload",
                    "--storage-connection-string-environment-variable"
                ]));
            Assert.Contains("requires a value", exception.Message);
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
