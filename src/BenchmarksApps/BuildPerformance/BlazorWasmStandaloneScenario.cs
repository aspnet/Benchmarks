using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Crank.EventSources;
using static Build.Program;

namespace Build
{
    class BlazorWasmStandaloneScenario
    {
        private readonly DotNet _dotnet;
        private readonly string _workingDirectory;

        public BlazorWasmStandaloneScenario(DotNet dotNet)
        {
            _dotnet = dotNet;
            _workingDirectory = dotNet.WorkingDirectory;
        }

        public async Task RunAsync()
        {
            await _dotnet.ExecuteAsync($"new blazorwasm");

            await Build();

            // Change to a .cs file
            await ChangeCSFile();

            // Changes to .razor file markup
            await ChangeToRazorMarkup();

            // Changes to .razor to include a parameter.
            await AddParameterToComponent();

            // No-Op build
            await NoOpBuild();

            // Rebuild
            await Rebuild();
        }

        async Task Build()
        {
            var buildDuration = await _dotnet.ExecuteAsync("build");

            MeasureAndRegister(
                "blazorwasm/first-build",
                buildDuration.TotalMilliseconds,
                "First build");
        }

        async Task ChangeCSFile()
        {
            // Changes to .cs file
            var csFile = Path.Combine(_workingDirectory, "Program.cs");
            File.AppendAllText(csFile, Environment.NewLine + "// Hello world");

            var buildDuration = await _dotnet.ExecuteAsync("build --no-restore");

            MeasureAndRegister(
                "blazorwasm/cs-file-change",
                buildDuration.TotalMilliseconds,
                "Change to cs file");
        }

        async Task ChangeToRazorMarkup()
        {
            var indexRazorFile = Path.Combine(_workingDirectory, "Pages", "Index.razor");
            File.AppendAllText(indexRazorFile, Environment.NewLine + "<h3>New content</h3>");

            var buildDuration = await _dotnet.ExecuteAsync("build --no-restore");

            MeasureAndRegister(
                "blazorwasm/razor-markup-change",
                buildDuration.TotalMilliseconds,
                "Change to .razor markup");
        }

        async Task Rebuild()
        {
            var buildDuration = await _dotnet.ExecuteAsync("build --no-restore --no-incremental");

            MeasureAndRegister(
                "blazorwasm/rebuild",
                buildDuration.TotalMilliseconds,
                "Rebuild");
        }

        async Task NoOpBuild()
        {
            var buildDuration = await _dotnet.ExecuteAsync("build --no-restore");

            MeasureAndRegister(
                "blazorwasm/noop-build",
                buildDuration.TotalMilliseconds,
                "Incremental build");
        }

        async Task AddParameterToComponent()
        {
            var counterRazorFile = Path.Combine(_workingDirectory, "Pages", "Counter.razor");
            var builder = new StringBuilder()
                .AppendLine()
                .AppendLine("@code {")
                .AppendLine("  [Parameter] public int IncrementAmount { get; set; }")
                .AppendLine("}");

            File.AppendAllText(counterRazorFile, builder.ToString());

            var buildDuration = await _dotnet.ExecuteAsync("build --no-restore");

            MeasureAndRegister(
                "blazorwasm/razor-add-parameter",
                buildDuration.TotalMilliseconds,
                "Add a parameter to .razor");
        }
    }
}
