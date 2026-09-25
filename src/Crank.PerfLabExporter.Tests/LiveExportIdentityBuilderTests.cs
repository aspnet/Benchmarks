// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Crank.PerfLabExporter.CommandLine;
using Crank.PerfLabExporter.Conversion;

namespace Crank.PerfLabExporter.Tests
{
    public class LiveExportIdentityBuilderTests
    {
        [Fact]
        public void BuildsIdentityFromCrankPropertiesAndDependencies()
        {
            var identity = LiveExportIdentityBuilder.Build(
                FixtureLoader.LoadExecution(),
                new LiveIdentityOptions());

            Assert.Equal("https://github.com/dotnet/runtime", identity.Build.Repo);
            Assert.Equal(
                "1111111111111111111111111111111111111111",
                identity.Build.GitHash);
            Assert.Equal(
                "Ubuntu.2204.Amd64.AspNetGold.Perf",
                identity.Lane.Queue);
            Assert.Equal("SUT+Load", identity.Lane.Configurations["Topology"]);
            Assert.Equal("Plaintext", identity.Scenario.Name);
            Assert.Equal("aspnet-plaintext", identity.Scenario.Family);
            Assert.Equal("12345", identity.AzureDevOps.BuildId);
            Assert.Equal("TrendBenchmarks", identity.Sql.Table);
        }

        [Fact]
        public void RequiresRunConfigurationProperties()
        {
            var execution = FixtureLoader.LoadExecution();
            execution.JobResults.Properties.Remove(
                "perflab.configuration.Topology");

            var exception = Assert.Throws<CrankConversionException>(() =>
                LiveExportIdentityBuilder.Build(
                    execution,
                    new LiveIdentityOptions()));

            Assert.Contains("configuration.Topology", exception.Message);
        }
    }
}
