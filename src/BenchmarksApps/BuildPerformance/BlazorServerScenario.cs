using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using static Build.Program;

namespace Build
{
    class BlazorServerScenario
    {
        private readonly DotNet _dotnet;
        private readonly string _workingDirectory;

        public BlazorServerScenario(DotNet dotnet)
        {
            _dotnet = dotnet;
            _workingDirectory = dotnet.WorkingDirectory;
        }

        public async Task RunAsync()
        {
            await _dotnet.ExecuteAsync($"new blazorserver");

            await Build();

            // Change to a .cs file
            await ChangeCSFile();

            // Changes to .razor file markup
            await ChangeToRazorMarkup();

            // Changes to .razor to include a parameter.
            await AddParameterToComponent();

            // No-Op build
            await NoOpBuild();

            // Changes to a .cshtml file
            await ModifyCshtmlFile();

            // Rebuild
            await Rebuild();
        }

        async Task Build()
        {
            var buildDuration = await _dotnet.ExecuteAsync("build");

            MeasureAndRegister(
                "blazorserver/first-build",
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
                "blazorserver/cs-file-change",
                buildDuration.TotalMilliseconds,
                "Change to cs file");
        }

        async Task ChangeToRazorMarkup()
        {
            var indexRazorFile = Path.Combine(_workingDirectory, "Pages", "Index.razor");
            File.AppendAllText(indexRazorFile, Environment.NewLine + "<h3>New content</h3>");

            var buildDuration = await _dotnet.ExecuteAsync("build --no-restore");

            MeasureAndRegister(
                "blazorserver/razor-markup-change",
                buildDuration.TotalMilliseconds,
                "Change to .razor markup");
        }

        async Task Rebuild()
        {
            var buildDuration = await _dotnet.ExecuteAsync("build --no-restore --no-incremental");

            MeasureAndRegister(
                "blazorserver/rebuild",
                buildDuration.TotalMilliseconds,
                "Rebuild");
        }

        async Task NoOpBuild()
        {
            var buildDuration = await _dotnet.ExecuteAsync("build --no-restore");

            MeasureAndRegister(
                "blazorserver/noop-build",
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
                "blazorserver/razor-add-parameter",
                buildDuration.TotalMilliseconds,
                "Add a parameter to .razor");
        }

        async Task ModifyCshtmlFile()
        {
            var file = Path.Combine(_workingDirectory, "Pages", "_Host.cshtml");
            var originalContent = File.ReadAllText(file);

            File.WriteAllText(file, originalContent.Replace("<body>", "<body><h2>Some text</h2>"));

            var buildDuration = await _dotnet.ExecuteAsync("build --no-restore");

            MeasureAndRegister(
                "blazorserver/razor-change-cshtml",
                buildDuration.TotalMilliseconds,
                "Change .cshtml file markup");
        }
    }
}
