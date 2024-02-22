using System.Diagnostics;
using Microsoft.Crank.EventSources;

namespace RunTemplate.Helpers;

internal sealed class AspNetProcess : IDisposable
{
    private readonly Process _process;
    private readonly TaskCompletionSource _applicationStartedTcs = new();
    private bool _disposed;

    public static AspNetProcess Start(string workingDirectory, string executableName, string urls)
    {
        var executablePath = Path.Combine(workingDirectory, executableName);
        var args = $"{executableName} --urls {urls}";

        var processStartInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            Arguments = args,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
        };

        var process = Process.Start(processStartInfo) ?? throw new InvalidOperationException("Unable to start process");

        BenchmarksEventSource.SetChildProcessId(process.Id);

        process.BeginOutputReadLine();

        return new(process);
    }

    private AspNetProcess(Process process)
    {
        _process = process;
        _process.OutputDataReceived += OnOutputDataReceived;
    }

    public Task WaitForApplicationStartAsync()
        => _applicationStartedTcs.Task;

    public async Task<int> WaitForExitAsync(CancellationToken cancellationToken = default)
    {
        await _process.WaitForExitAsync(cancellationToken);
        return _process.ExitCode;
    }

    private void OnOutputDataReceived(object sender, DataReceivedEventArgs e)
    {
        const string ExpectedApplicationStartedMessage = "Application started";

        if (e.Data is not { } data)
        {
            return;
        }

        Console.WriteLine(data);

        if (!_applicationStartedTcs.Task.IsCompleted &&
            data.Contains(ExpectedApplicationStartedMessage, StringComparison.Ordinal))
        {
            _applicationStartedTcs.SetResult();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (!_process.HasExited)
        {
            _process.Kill();
            _process.WaitForExit();
        }

        _process.OutputDataReceived -= OnOutputDataReceived;

        _applicationStartedTcs.TrySetCanceled();

        _process.Dispose();
    }
}
