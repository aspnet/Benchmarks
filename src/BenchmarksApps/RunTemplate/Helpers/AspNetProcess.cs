using System.Diagnostics;
using Microsoft.Crank.EventSources;

namespace RunTemplate.Helpers;

internal sealed class AspNetProcess : IDisposable
{
    private readonly Process _process;
    private readonly TaskCompletionSource _applicationStartedTcs = new();
    private bool _disposed;

    public static AspNetProcess Start(DotNet dotnet, string workingDirectory, string relativeFileName, string urls)
    {
        var args = $"{relativeFileName} --urls {urls}";

        var process = dotnet.Execute(args, processStartInfo =>
        {
            processStartInfo.WorkingDirectory = workingDirectory;
            processStartInfo.RedirectStandardOutput = true;
        });

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
