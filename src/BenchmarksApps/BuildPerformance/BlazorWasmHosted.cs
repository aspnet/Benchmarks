using System;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Crank.EventSources;
using static Build.Program;

namespace Build
{
    class BlazorWasmHosted
    {
        private const string ProjectName = "BlazorApp";

        private readonly DotNet _dotnet;
        private readonly string _workingDirectory;
        private readonly string _serverDirectory;
        private readonly string _clientDirectory;

        public BlazorWasmHosted(DotNet dotNet)
        {
            _dotnet = dotNet;
            _workingDirectory = dotNet.WorkingDirectory;
            _serverDirectory = Path.Combine(_workingDirectory, ProjectName);
            _clientDirectory = Path.Combine(_workingDirectory, $"{ProjectName}.Client");
        }

        public async Task RunAsync()
        {
            await _dotnet.ExecuteAsync($"new blazor -n {ProjectName} -int WebAssembly");

            await Build();

            // Changes to .razor file markup
            await ChangeClient();

            // Changes to .razor to include a parameter.
            await ChangeServer();

            // No-Op build
            await NoOpBuild();

            // Rebuild
            await Rebuild();
        }

        async Task Build()
        {
            var buildDuration = await _dotnet.ExecuteAsync("build", _serverDirectory);

            MeasureAndRegister(
                "blazorwasm-hosted/first-build",
                buildDuration.TotalMilliseconds,
                "First build");
        }

        async Task ChangeServer()
        {
            var csFile = Path.Combine(_serverDirectory, "Program.cs");
            File.AppendAllText(csFile, Environment.NewLine + "// Hello world");

            var buildDuration = await _dotnet.ExecuteAsync("build --no-restore", _serverDirectory);

            MeasureAndRegister(
                "blazorwasm-hosted/change-server",
                buildDuration.TotalMilliseconds,
                "Change a file in Server");
        }

        async Task ChangeClient()
        {
            var indexRazorFile = Path.Combine(_clientDirectory, "Pages", "Counter.razor");
            File.AppendAllText(indexRazorFile, Environment.NewLine + "<h3>New content</h3>");

            var buildDuration = await _dotnet.ExecuteAsync("build --no-restore", _serverDirectory);

            MeasureAndRegister(
                "blazorwasm-hosted/client-file",
                buildDuration.TotalMilliseconds,
                "Change a file in Client");
        }

        async Task Rebuild()
        {
            var buildDuration = await _dotnet.ExecuteAsync("build --no-restore --no-incremental", _serverDirectory);

            MeasureAndRegister(
                "blazorwasm-hosted/rebuild",
                buildDuration.TotalMilliseconds,
                "Rebuild");
        }

        async Task NoOpBuild()
        {
            var buildDuration = await _dotnet.ExecuteAsync("build --no-restore", _serverDirectory);

            MeasureAndRegister(
                "blazorwas-hostedm/noop-build",
                buildDuration.TotalMilliseconds,
                "Incremental build");
        }
    }
}
