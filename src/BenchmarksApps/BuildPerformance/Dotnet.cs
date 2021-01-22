using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace Build
{
    public sealed class DotNet
    {
        private readonly string _dotnetPath;
        private readonly Stopwatch _stopWatch;

        private DotNet(string dotnetPath, string workingDirectory, bool verbose, bool performanceSummary)
        {
            _dotnetPath = dotnetPath;
            WorkingDirectory = workingDirectory;
            _verbose = verbose;
            _performanceSummary = performanceSummary;
            _stopWatch = new Stopwatch();
        }

        public string WorkingDirectory { get; }

        private readonly bool _verbose;
        private readonly bool _performanceSummary;

        public static DotNet Initialize(string workingDirectory, bool verbose, bool performanceSummary)
        {
            var muxerPath = Environment.GetEnvironmentVariable("DOTNET_EXE") ?? "dotnet";
            Console.WriteLine($"Muxer path: {muxerPath}.");
            return new DotNet(muxerPath, workingDirectory, verbose, performanceSummary);
        }

        public async ValueTask<TimeSpan> ExecuteAsync(string args, string? workingDir = null)
        {
            if (args.StartsWith("build ", StringComparison.Ordinal))
            {
                if (_verbose)
                {
                    args += " /v:N";
                }

                if (_performanceSummary)
                {
                    args += " /clp:PerformanceSummary";
                }
            }

            _stopWatch.Restart();
            var process = Process.Start(new ProcessStartInfo
            {
                FileName = _dotnetPath,
                Arguments = args,
                WorkingDirectory = workingDir ?? WorkingDirectory,
                UseShellExecute = false,
                Environment =
                {
                    ["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1",
                    ["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1",
                },
            });

            if (process is null)
            {
                throw new InvalidOperationException("Failed to launch dotnet");
            }

            await process.WaitForExitAsync();
            _stopWatch.Stop();
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException($"{_dotnetPath} {args} failed with exit code {process.ExitCode}");
            }

            return _stopWatch.Elapsed;
        }
    }
}