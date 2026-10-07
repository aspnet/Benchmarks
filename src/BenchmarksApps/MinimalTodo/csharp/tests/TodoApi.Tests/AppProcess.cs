using System.Collections.Concurrent;
using System.Diagnostics;

namespace TodoApi.Tests;

internal sealed class AppProcess : IDisposable
{
    private readonly ConcurrentQueue<string> output = new();
    private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Process process;
    public int Id => process.Id;
    public int ExitCode => process.ExitCode;
    public string[] Output => output.ToArray();

    public AppProcess(string port)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = AppContext.BaseDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        start.ArgumentList.Add(typeof(Program).Assembly.Location);
        start.Environment["PORT"] = port;
        start.Environment["DOTNET_ENVIRONMENT"] = "Production";
        start.Environment["ASPNETCORE_ENVIRONMENT"] = "Production";
        process = new Process { StartInfo = start };
        process.OutputDataReceived += Capture;
        process.ErrorDataReceived += Capture;
        if (!process.Start())
        {
            throw new InvalidOperationException("Could not start the API process.");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
    }

    private void Capture(object sender, DataReceivedEventArgs args)
    {
        if (args.Data is { } line)
        {
            output.Enqueue(line);
            if (line == "Application started.")
            {
                ready.TrySetResult();
            }
        }
    }

    public async Task WaitForReadyAsync()
    {
        try
        {
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }
        catch (TimeoutException exception)
        {
            throw new TimeoutException($"Readiness timed out. Output:\n{string.Join('\n', Output)}", exception);
        }
    }

    public async Task WaitForExitAsync()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            await process.WaitForExitAsync(deadline.Token);
        }
        catch (OperationCanceledException exception)
        {
            throw new TimeoutException(
                $"Exit exceeded 10 seconds; cleanup will force termination. Output:\n{string.Join('\n', Output)}",
                exception);
        }
    }

    public void Dispose()
    {
        if (!process.HasExited)
        {
            process.Kill(entireProcessTree: true);
            if (!process.WaitForExit(5000))
            {
                throw new TimeoutException($"Owned API process {process.Id} did not exit after forced cleanup.");
            }
        }

        process.Dispose();
    }
}
