// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Concurrent;

namespace System.Net.Benchmarks;

internal abstract class BenchmarkServer<TListener, TAcceptResult, TOptions> : BenchmarkApp<TOptions>
    where TListener : IListener<TAcceptResult>
    where TOptions : IBenchmarkServerOptions, new()
{
    // Maximum time to wait for in-flight accepted-connection tasks to drain after
    // cancellation before letting the listener dispose. Keep this comfortably under
    // BenchmarkApp.s_shutdownDeadline so the outer deadline still has room to fire
    // if disposal itself stalls.
    private static readonly TimeSpan s_drainTimeout = TimeSpan.FromSeconds(10);

    private static int s_errors;

    protected abstract Task<TListener> ListenAsync(TOptions options, CancellationToken ct);
    protected virtual string GetReadyStateText(TListener listener) => $"Listening on {listener.LocalEndPoint}";
    protected abstract Task ProcessAcceptedAsync(TAcceptResult accepted, TOptions options, CancellationToken ct);
    protected virtual bool IsExpectedException(Exception e) => false;

    protected override async Task RunBenchmarkAsync(TOptions options, CancellationToken ct)
    {
        await using var listener = await ListenAsync(options, ct);
        Log(GetReadyStateText(listener));

        // Track per-connection tasks so we can drain them on cancellation before
        // the listener disposes. Without this, `await using var listener` can race
        // against in-flight TLS/QUIC connections — SslStream.DisposeAsync attempts
        // to send a close_notify alert, and if the underlying socket is being torn
        // down concurrently, the dispose can stall long enough to strand the port
        // for the next benchmark run.
        var inflight = new ConcurrentDictionary<Task, byte>();
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var accepted = await listener.AcceptAsync(ct).ConfigureAwait(false);
                var task = Task.Run(() => ProcessAcceptedNoThrowAsync(accepted, options, ct), ct);
                inflight[task] = 0;
                _ = task.ContinueWith(static (t, state) => ((ConcurrentDictionary<Task, byte>)state!).TryRemove(t, out _),
                    inflight, TaskScheduler.Default);
            }
        }
        catch (OperationCanceledException e) when (e.CancellationToken == ct) { }
        finally
        {
            await DrainAsync(inflight).ConfigureAwait(false);

            if (s_errors > 0)
            {
                LogMetric(MetricName.Errors, "Errors", s_errors, "n0");
            }
        }
        Log("Exiting...");
    }

    private static async Task DrainAsync(ConcurrentDictionary<Task, byte> inflight)
    {
        if (inflight.IsEmpty)
        {
            return;
        }

        var pending = Task.WhenAll(inflight.Keys);
        var completed = await Task.WhenAny(pending, Task.Delay(s_drainTimeout)).ConfigureAwait(false);
        if (completed != pending)
        {
            Log($"Drain timeout ({s_drainTimeout.TotalSeconds:n0}s) reached with {inflight.Count} in-flight connection task(s) still running; continuing shutdown.");
        }
    }

    private async Task ProcessAcceptedNoThrowAsync(TAcceptResult accepted, TOptions options, CancellationToken ct)
    {
        try
        {
            await ProcessAcceptedAsync(accepted, options, ct);
        }
        catch (OperationCanceledException e) when (e.CancellationToken == ct) { }
        catch (Exception e) when (IsExpectedException(e)) { }
        catch (Exception e)
        {
            Interlocked.Increment(ref s_errors);
            Log($"Exception occurred: {e}");
        }
    }
}
