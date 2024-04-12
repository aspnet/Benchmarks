// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using static System.Net.Benchmarks.Logger;

namespace System.Net.Benchmarks;

internal interface IBaseListener<TAcceptResult> : IAsyncDisposable
{
    Task<TAcceptResult> AcceptAsync(CancellationToken cancellationToken);
}

internal abstract class BaseServer<TAcceptResult, TOptions> : BenchmarkApp<TOptions>
{
    private int _errors;

    protected abstract Task<IBaseListener<TAcceptResult>> ListenAsync(TOptions options, CancellationToken ct);
    protected abstract string GetReadyStateText(IBaseListener<TAcceptResult> listener);
    protected abstract Task ProcessAsyncInternal(TAcceptResult accepted, TOptions options, CancellationToken ct);
    protected virtual bool IsExpectedException(Exception e) => false;

    protected override async Task RunAsync(TOptions options)
    {
        OnStartup(options);

        var ctrlC = new CancellationTokenSource();
        Console.CancelKeyPress += (s, e) =>
        {
            Log("Shutting down...");
            e.Cancel = true;
            ctrlC.Cancel();
        };

        RegisterSimpleMetric($"{MetricPrefix}/errors", "Errors", "n0");

        await using var listener = await ListenAsync(options, ctrlC.Token);
        Log(GetReadyStateText(listener));

        try
        {
            while (!ctrlC.IsCancellationRequested)
            {
                var accepted = await listener.AcceptAsync(ctrlC.Token).ConfigureAwait(false);
                _ = Task.Run(() => ProcessAsync(accepted, options, ctrlC.Token));
            }
        }
        catch (OperationCanceledException e) when (e.CancellationToken == ctrlC.Token) { }
        finally
        {
            if (_errors > 0)
            {
                LogMetric($"{MetricPrefix}/errors", _errors);
            }
        }
        Log("Exiting...");
    }

    private async Task ProcessAsync(TAcceptResult accepted, TOptions options, CancellationToken ct)
    {
        try
        {
            await ProcessAsyncInternal(accepted, options, ct);
        }
        catch (OperationCanceledException e) when (e.CancellationToken == ct) { }
        catch (Exception e) when (IsExpectedException(e)) { }
        catch (Exception e)
        {
            Interlocked.Increment(ref _errors);
            Log($"Exception occurred: {e}");
        }
    }
}
