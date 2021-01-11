using System;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Crank.EventSources;

namespace Build
{
    static class Program
    {
        static Task<int> Main(string[] args)
        {
            var rootCommand = new RootCommand
            {
                new Option<string>(new[] { "--scenario", "-s" }, "Scenario"),
                new Option<bool>(new[] { "--verbose", "-v" }, "Verbose msbuild logs"),
            };

            Console.WriteLine(string.Join(" ", args));

            rootCommand.Handler = CommandHandler.Create(async (string scenario, bool verbose) =>
            {
                var workingDirectory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
                Directory.CreateDirectory(workingDirectory);
                Console.WriteLine($"Running scenario {scenario}");

                var dotnet = DotNet.Initialize(workingDirectory, verbose);

                switch (scenario)
                {
                    case "blazorserver":
                        await new BlazorServerScenario(dotnet).RunAsync();
                        break;

                    case "blazorwasm":
                        await new BlazorWasmStandaloneScenario(dotnet).RunAsync();
                        break;

                    case "blazorwasm-hosted":
                        await new BlazorWasmHosted(dotnet).RunAsync();
                        break;

                    case "mvc":
                        await new MvcScenario(dotnet).RunAsync();
                        break;

                    case "api":
                        await new ApiScenario(dotnet).RunAsync();
                        break;

                    default:
                        throw new InvalidOperationException($"Unknown scenario {scenario}.");
                }

            });

            return rootCommand.InvokeAsync(args);
        }

        public static void MeasureAndRegister(string name, double value, string description, string format = "n2")
        {
            BenchmarksEventSource.Register(name, Operations.First, Operations.First, description, description, format);
            BenchmarksEventSource.Measure(name, value);
        }
    }
}
