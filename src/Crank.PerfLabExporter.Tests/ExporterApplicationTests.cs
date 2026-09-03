// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Text.Json;
using Crank.PerfLabExporter.Contracts;
using Crank.PerfLabExporter.Contracts.PerfLab;

namespace Crank.PerfLabExporter.Tests
{
    public class ExporterApplicationTests
    {
        [Fact]
        public async Task ConvertCommandWritesDeterministicReportWithoutNetwork()
        {
            var outputDirectory = Path.Combine(
                AppContext.BaseDirectory,
                "live-convert-output",
                Guid.NewGuid().ToString("N"));
            try
            {
                using var output = new StringWriter();
                using var error = new StringWriter();
                var application = new ExporterApplication(output, error);

                var exitCode = await application.RunAsync(
                [
                    "convert",
                    "--crank-json",
                    FixtureLoader.GetPath("crank-result.json"),
                    "--counter-policy",
                    FixtureLoader.GetPath("counter-policy.json"),
                    "--identity-source",
                    "crank",
                    "--output-directory",
                    outputDirectory
                ]);

                Assert.Equal(0, exitCode);
                var resultPath = Assert.Single(
                    Directory.GetFiles(outputDirectory, "*.perflab.json"));
                var report = JsonSerializer.Deserialize<PerfLabReport>(
                    File.ReadAllText(resultPath),
                    ContractJson.CreateSerializerOptions());
                Assert.NotNull(report);
                Assert.Equal("aspnet-plaintext", report.Run.Name);
                var test = Assert.Single(report.Tests);
                Assert.Equal("Plaintext", test.Name);
                Assert.Equal("SUT+Load", report.Run.Configurations["Topology"]);
                Assert.Equal(
                    "crank-perflab-counter-policy.json",
                    test.AdditionalData[
                        "crank.property.perflab.policy.path"]);
                Assert.EndsWith(
                    "counter-policy.json",
                    test.AdditionalData["crank.counterPolicyPath"],
                    StringComparison.Ordinal);
            }
            finally
            {
                if (Directory.Exists(outputDirectory))
                {
                    Directory.Delete(outputDirectory, recursive: true);
                }
            }
        }
    }
}
