// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.CommandLine;
using System.CommandLine.Parsing;
using System.Runtime.InteropServices;

namespace System.Net.Benchmarks;

public abstract class BenchmarkApp<TOptions> where TOptions : new()
{
    // Maximum time to wait for graceful shutdown after cancellation is signaled
    // before forcing process exit. Crank sends SIGTERM, waits 5s, then SIGINT,
    // so we keep our deadline well under any subsequent SIGKILL.
    private static readonly TimeSpan s_shutdownDeadline = TimeSpan.FromSeconds(15);

    private static bool s_appStarted;

    protected static CancellationTokenSource GlobalCts { get; } = new();

    protected abstract string Name { get; }
    protected abstract string MetricPrefix { get; }

    protected static void Log(string message) => LogHelper.Log(message);

    protected void LogMetric(string name, string description, double value, string format = "n2")
    {
        var metricName = MetricPrefix + name;
        LogHelper.RegisterSimpleMetric(metricName, description, format);
        LogHelper.LogMetric(metricName, value);
    }

    protected void LogMetricPercentiles(string name, string description, List<double> values, string format = "n3")
    {
        var metricName = MetricPrefix + name;
        LogHelper.RegisterPercentileMetric(metricName, description, description, format);
        LogHelper.LogPercentileMetric(metricName, values);
    }

    protected virtual void ValidateOptions(TOptions options) { }
    protected abstract Task RunBenchmarkAsync(TOptions options, CancellationToken cancellationToken);

    public Task<int> RunAsync<TBinder>(string[] args)
        where TBinder : BenchmarkOptionsBinder<TOptions>, new()
    {
        if (s_appStarted)
        {
            throw new InvalidOperationException($"{Name} is already running.");
        }
        s_appStarted = true;

        var rootCommand = new RootCommand(Name);
        var binder = new TBinder();
        binder.AddCommandLineArguments(rootCommand);
        rootCommand.SetHandler(RunAsyncInternal, binder);

        return rootCommand.InvokeAsync(args);
    }

    private async Task<int> RunAsyncInternal(TOptions options)
    {
        Log($"Starting {Name}");
        Log($"Options:");
        Log($"{options}");

        ValidateOptions(options);

        Console.CancelKeyPress += static (s, e) =>
        {
            Log("Keyboard interrupt...");
            e.Cancel = true;
            GlobalCts.Cancel();
        };

        // Crank's Linux agent stops jobs with SIGTERM first (5 s grace) then SIGINT.
        // Without an explicit SIGTERM handler, the .NET runtime's default behavior
        // terminates the process after a short ProcessExit window and any in-flight
        // TLS/QUIC connections never observe cancellation. That can leave sockets
        // in a half-broken state, stranding the listen port for the next benchmark.
        // Register SIGTERM/SIGQUIT here so GlobalCts.Cancel runs the same shutdown
        // path on Linux as Ctrl+C does on Windows.
        using var sigTerm = PosixSignalRegistration.Create(PosixSignal.SIGTERM, static ctx =>
        {
            Log("SIGTERM received...");
            ctx.Cancel = true;
            GlobalCts.Cancel();
        });
        using var sigQuit = PosixSignalRegistration.Create(PosixSignal.SIGQUIT, static ctx =>
        {
            Log("SIGQUIT received...");
            ctx.Cancel = true;
            GlobalCts.Cancel();
        });

        // NOTE:
        // It is better for metrics to be registered with some delay to ensure the metadata is collected.
        // Event listener is started (shortly) after the benchmark app starts, so it might miss the registration event.
        _ = Task.Run(static async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(1), GlobalCts.Token).ConfigureAwait(false);
            LogHelper.RegisterSimpleMetric("env/processorcount", "Processor Count", "n0");
            LogHelper.LogMetric("env/processorcount", Environment.ProcessorCount);
        });

        // Run the benchmark, but enforce a hard shutdown deadline once cancellation
        // has been requested. If RunBenchmarkAsync hangs in disposal (for example a
        // stuck SslStream close_notify or QuicConnection drain), Environment.Exit
        // guarantees we release the port for the next run instead of waiting for
        // crank's SIGKILL fallback.
        try
        {
            var benchmarkTask = RunBenchmarkAsync(options, GlobalCts.Token);
            await WaitWithShutdownDeadlineAsync(benchmarkTask).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            Log("Unhandled exception: " + e);
            throw;
        }
        return 0; // on success, returning 0 explicitly; otherwise RootCommand (?) will not report unhandled exceptions to crank as failures
    }

    private static async Task WaitWithShutdownDeadlineAsync(Task benchmarkTask)
    {
        // Fast path: benchmark finished (or threw) before cancellation was ever requested.
        await Task.WhenAny(benchmarkTask, WaitForCancellationAsync(GlobalCts.Token)).ConfigureAwait(false);

        if (benchmarkTask.IsCompleted)
        {
            await benchmarkTask.ConfigureAwait(false);
            return;
        }

        // Cancellation has been signaled. Give the benchmark a bounded time to wind
        // down gracefully, then force-exit so we don't strand the port between runs.
        var completed = await Task.WhenAny(benchmarkTask, Task.Delay(s_shutdownDeadline)).ConfigureAwait(false);
        if (completed == benchmarkTask)
        {
            await benchmarkTask.ConfigureAwait(false);
            return;
        }

        Log($"Shutdown deadline ({s_shutdownDeadline.TotalSeconds:n0}s) exceeded after cancellation; forcing process exit.");
        // Use a non-zero exit code so crank reports the run as failed. BenchmarkClient
        // uses GlobalCts.CancelAfter as a watchdog when a scenario hangs; if we forced
        // exit(0) here, that timeout would silently be reported as a successful run.
        Environment.Exit(124);
    }

    private static Task WaitForCancellationAsync(CancellationToken ct)
    {
        if (ct.IsCancellationRequested)
        {
            return Task.CompletedTask;
        }
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ct.Register(static state => ((TaskCompletionSource)state!).TrySetResult(), tcs);
        return tcs.Task;
    }
}
