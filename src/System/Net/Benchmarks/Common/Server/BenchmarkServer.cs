// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

namespace System.Net.Benchmarks;

internal abstract class BenchmarkServer<TListener, TAcceptResult, TOptions> : BenchmarkApp<TOptions>
    where TListener : IListener<TAcceptResult>
    where TOptions : IBenchmarkServerOptions, new()
{
    // Maximum time to wait for in-flight accepted-connection tasks to drain after
    // cancellation before letting the listener dispose. Keep this comfortably under
    // BenchmarkApp's shutdown deadline so the outer deadline still has room to fire
    // if disposal itself stalls. Note: this drain only tracks the outer
    // ProcessAcceptedNoThrowAsync task per accepted connection. Inner per-stream
    // Task.Run operations spawned by TlsBenchmarkServer.AcceptStreamsAsync are NOT
    // individually tracked here; if those hang during shutdown, the BenchmarkApp
    // hard-exit deadline is the final safety net.
    private static readonly TimeSpan s_drainTimeout = TimeSpan.FromSeconds(10);

    private static int s_errors;
    private static int s_inflight;

    protected abstract Task<TListener> ListenAsync(TOptions options, CancellationToken ct);
    protected virtual string GetReadyStateText(TListener listener) => $"Listening on {listener.LocalEndPoint}";
    protected abstract Task ProcessAcceptedAsync(TAcceptResult accepted, TOptions options, CancellationToken ct);
    protected virtual bool IsExpectedException(Exception e) => false;

    protected override async Task RunBenchmarkAsync(TOptions options, CancellationToken ct)
    {
        await using var listener = await ListenAsync(options, ct);
        Log(GetReadyStateText(listener));

        try
        {
            while (!ct.IsCancellationRequested)
            {
                var accepted = await listener.AcceptAsync(ct).ConfigureAwait(false);
                Interlocked.Increment(ref s_inflight);
                // Schedule the cleanup work unconditionally (no cancellation token
                // on Task.Run). If we passed ct here and cancellation raced the
                // accept, Task.Run would refuse to start the delegate and we'd leak
                // the accepted socket / QuicConnection. Cancellation is observed
                // inside ProcessAcceptedNoThrowAsync via the captured token.
                _ = Task.Run(() => ProcessAcceptedNoThrowAsync(accepted, options, ct));
            }
        }
        catch (OperationCanceledException e) when (e.CancellationToken == ct) { }
        finally
        {
            await DrainAsync().ConfigureAwait(false);

            if (s_errors > 0)
            {
                LogMetric(MetricName.Errors, "Errors", s_errors, "n0");
            }
        }
        Log("Exiting...");
    }

    private static async Task DrainAsync()
    {
        // Spin-wait via short polled delays. We intentionally avoid storing per-task
        // handles or scheduling per-task continuations to keep the accept hot path
        // free of bookkeeping that would bias TLS/QUIC handshake measurements.
        if (Volatile.Read(ref s_inflight) == 0)
        {
            return;
        }

        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (Volatile.Read(ref s_inflight) > 0 && sw.Elapsed < s_drainTimeout)
        {
            await Task.Delay(50).ConfigureAwait(false);
        }

        var remaining = Volatile.Read(ref s_inflight);
        if (remaining > 0)
        {
            Log($"Drain timeout ({s_drainTimeout.TotalSeconds:n0}s) reached with {remaining} in-flight connection task(s) still running; continuing shutdown.");
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
        finally
        {
            Interlocked.Decrement(ref s_inflight);
        }
    }
}
