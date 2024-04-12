// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.CommandLine;
using System.CommandLine.Binding;

using static Common.Logger;

namespace Common;

internal interface IBaseListener<TAcceptResult> : IAsyncDisposable
{
    Task<TAcceptResult> AcceptAsync(CancellationToken cancellationToken);
}

internal abstract class BaseServer<TAcceptResult, TOptions>
{
    private int _errors;

    public abstract string Name { get; }
    public abstract string MetricPrefix { get; }
    public abstract void AddCommandLineOptions(RootCommand command);
    public virtual void ValidateOptions(TOptions options) { }
    public abstract Task<IBaseListener<TAcceptResult>> ListenAsync(TOptions options, CancellationToken ct);
    public abstract string GetReadyStateText(IBaseListener<TAcceptResult> listener);
    public abstract Task ProcessAsyncInternal(TAcceptResult accepted, TOptions options, CancellationToken ct);
    public virtual bool IsExpectedException(Exception e) => false;

    public Task RunCommandAsync<TBinder>(string[] args) where TBinder : BinderBase<TOptions>, new()
    {
        var rootCommand = new RootCommand(Name);
        AddCommandLineOptions(rootCommand);
        rootCommand.SetHandler<TOptions>(RunAsync, new TBinder());
        return rootCommand.InvokeAsync(args);
    }

    private async Task RunAsync(TOptions options)
    {
        Log($"Starting {Name}");
        Log($"Options:");
        Log($"{options}");

        ValidateOptions(options);

        RegisterSimpleMetric("env/processorcount", "Processor Count", "n0");
        LogMetric("env/processorcount", Environment.ProcessorCount);

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
