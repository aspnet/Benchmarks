using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace Build
{
    public readonly struct DotNet
    {
        private readonly string _dotnetPath;
        private readonly Stopwatch _stopWatch;

        private DotNet(string dotnetPath, string workingDirectory, bool verbose)
        {
            _dotnetPath = dotnetPath;
            WorkingDirectory = workingDirectory;
            _verbose = verbose;
            _stopWatch = new Stopwatch();
        }

        public string WorkingDirectory { get; }

        private readonly bool _verbose;

        public static DotNet Initialize(string workingDirectory, bool verbose)
        {
            var muxerPath = Environment.GetEnvironmentVariable("DOTNET_EXE") ?? "dotnet";
            Console.WriteLine($"Muxer path: {muxerPath}.");
            return new DotNet(muxerPath, workingDirectory, verbose);
        }

        public async ValueTask<TimeSpan> ExecuteAsync(string args, string? workingDir = null)
        {
            if (_verbose && args.StartsWith("build ", StringComparison.Ordinal))
            {
                args += " /v:N";
            }

            var process = Process.Start(new ProcessStartInfo
            {
                FileName = _dotnetPath,
                Arguments = args,
                WorkingDirectory = workingDir ?? WorkingDirectory,
            });

            if (process is null)
            {
                throw new InvalidOperationException("Failed to launch dotnet");
            }

            _stopWatch.Restart();
            await process.WaitForExitAsync();
            return _stopWatch.Elapsed;
        }
    }
}