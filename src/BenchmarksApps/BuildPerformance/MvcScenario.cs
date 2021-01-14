using System;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Crank.EventSources;
using static Build.Program;

namespace Build
{
    class MvcScenario
    {
        private readonly DotNet _dotnet;
        private readonly string _workingDirectory;

        public MvcScenario(DotNet dotnet)
        {
            _dotnet = dotnet;
            _workingDirectory = dotnet.WorkingDirectory;
        }

        public async Task RunAsync()
        {
            await _dotnet.ExecuteAsync($"new mvc");

            await Build();

            // Change to a .cs file
            await ChangeCSFile();

            // Changes to .cshtml file
            await ChangeCshtmlFile();

            // No-Op build
            await NoOpBuild();

            // Rebuild
            await Rebuild();
        }

        async Task Build()
        {
            var buildDuration = await _dotnet.ExecuteAsync("build");

            MeasureAndRegister(
                "mvc/first-build",
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
                "mvc/cs-file-change",
                buildDuration.TotalMilliseconds,
                "Change to cs file");
        }

        async Task Rebuild()
        {
            var buildDuration = await _dotnet.ExecuteAsync("build --no-restore --no-incremental");

            MeasureAndRegister(
                "mvc/rebuild",
                buildDuration.TotalMilliseconds,
                "Rebuild");
        }

        async Task NoOpBuild()
        {
            var buildDuration = await _dotnet.ExecuteAsync("build --no-restore");

            MeasureAndRegister(
                "mvc/noop-build",
                buildDuration.TotalMilliseconds,
                "Incremental build");
        }

        async Task ChangeCshtmlFile()
        {
            var file = Path.Combine(_workingDirectory, "Views", "Home", "Index.cshtml");
            var originalContent = File.ReadAllText(file);

            File.WriteAllText(file, originalContent.Replace("<body>", "<body><h2>Some text</h2>"));

            var buildDuration = await _dotnet.ExecuteAsync("build --no-restore");

            MeasureAndRegister(
                "mvc/razor-change-cshtml",
                buildDuration.TotalMilliseconds,
                "Change .cshtml file markup");
        }
    }
}
