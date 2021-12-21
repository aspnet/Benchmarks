using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Crank.EventSources;
using static Build.Program;

namespace Build
{
    class ApiScenario
    {
        private readonly DotNet _dotnet;
        private readonly string _workingDirectory;

        public ApiScenario(DotNet dotnet)
        {
            _dotnet = dotnet;
            _workingDirectory = dotnet.WorkingDirectory;
        }

        public async Task RunAsync()
        {
#if NET7_0_OR_GREATER            
            await _dotnet.ExecuteAsync($"new webapi");
#else
            await _dotnet.ExecuteAsync($"new api");
#end
            await Build();

            // Change to a .cs file
            await ChangeCSFile();

            // No-Op build
            await NoOpBuild();

            // Rebuild
            await Rebuild();
        }

        async Task Build()
        {
            var buildDuration = await _dotnet.ExecuteAsync("build");

            MeasureAndRegister(
                "api/first-build",
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
                "api/cs-file-change",
                buildDuration.TotalMilliseconds,
                "Change to cs file");
        }

        async Task Rebuild()
        {
            var buildDuration = await _dotnet.ExecuteAsync("build --no-restore --no-incremental");

            MeasureAndRegister(
                "api/rebuild",
                buildDuration.TotalMilliseconds,
                "Rebuild");
        }

        async Task NoOpBuild()
        {
            var buildDuration = await _dotnet.ExecuteAsync("build --no-restore");

            MeasureAndRegister(
                "api/noop-build",
                buildDuration.TotalMilliseconds,
                "Incremental build");
        }
    }
}
