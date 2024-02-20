using System.Diagnostics;

namespace RunTemplate.Helpers;

internal sealed class DotNet
{
    private readonly string _dotnetPath;
    private readonly bool _verbose;

    public string WorkingDirectory { get; }

    private DotNet(string dotnetPath, string workingDirectory, bool verbose)
    {
        _dotnetPath = dotnetPath;
        WorkingDirectory = workingDirectory;
        _verbose = verbose;
    }

    public static DotNet Create(string workingDirectory, bool verbose)
    {
        var muxerPath = Environment.GetEnvironmentVariable("DOTNET_EXE") ?? "dotnet";
        Console.WriteLine($"Muxer path: {muxerPath}.");
        return new DotNet(muxerPath, workingDirectory, verbose);
    }

    public Process Execute(string args, Action<ProcessStartInfo>? configure = null)
    {
        if (args.StartsWith("build", StringComparison.Ordinal)  && _verbose)
        {
            args += " /v:N";
        }

        var processStartInfo = new ProcessStartInfo
        {
            FileName = _dotnetPath,
            Arguments = args,
            WorkingDirectory = WorkingDirectory,
            UseShellExecute = false,
            Environment =
            {
                ["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1",
                ["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1",
            },
        };

        configure?.Invoke(processStartInfo);

        return Process.Start(processStartInfo) ?? throw new InvalidOperationException("Failed to launch dotnet");
    }

    public async ValueTask ExecuteAsync(string args, Action<ProcessStartInfo>? configure = null)
    {
        using var process = Execute(args, configure);
        await process.WaitForExitAsync();
    }
}
