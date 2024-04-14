// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

namespace System.Net.Benchmarks;

internal interface IBaseListener<TAcceptResult> : IAsyncDisposable
{
    Task<TAcceptResult> AcceptAsync(CancellationToken cancellationToken);
}

internal interface IBenchmarkServerOptions : IBenchmarkOptions
{
}

internal abstract class BaseServer<TAcceptResult, TOptions> : BenchmarkApp<TOptions>
    where TOptions : IBenchmarkServerOptions, new()
{
    private static int s_errors;

    protected abstract Task<IBaseListener<TAcceptResult>> ListenAsync(TOptions options, CancellationToken ct);
    protected abstract string GetReadyStateText(IBaseListener<TAcceptResult> listener);
    protected abstract Task ProcessAcceptedAsync(TAcceptResult accepted, TOptions options, CancellationToken ct);
    protected virtual bool IsExpectedException(Exception e) => false;

    protected override async Task RunBenchmarkAsync(TOptions options, CancellationToken ct)
    {
        RegisterMetric(MetricName.Errors, "Errors", "n0");

        await using var listener = await ListenAsync(options, ct);
        Log(GetReadyStateText(listener));

        try
        {
            while (!ct.IsCancellationRequested)
            {
                var accepted = await listener.AcceptAsync(ct).ConfigureAwait(false);
                _ = Task.Run(() => ProcessAcceptedNoThrowAsync(accepted, options, ct), ct);
            }
        }
        catch (OperationCanceledException e) when (e.CancellationToken == ct) { }
        finally
        {
            if (s_errors > 0)
            {
                LogMetric(MetricName.Errors, s_errors);
            }
        }
        Log("Exiting...");
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
