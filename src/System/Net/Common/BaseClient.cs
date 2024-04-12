// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.CommandLine;
using System.CommandLine.Binding;

using static System.Net.Benchmarks.Logger;

namespace System.Net.Benchmarks;

internal abstract class BenchmarkApp<TOptions>
{
    private static bool s_appStarted;

    protected abstract string Name { get; }
    protected abstract string MetricPrefix { get; }
    protected abstract void AddCommandLineOptions(RootCommand command);
    protected abstract void ValidateOptions(TOptions options);
    protected abstract Task RunAsync(TOptions options);

    public Task RunCommandAsync<TBinder>(string[] args)
        where TBinder : BinderBase<TOptions>, new()
    {
        if (s_appStarted)
        {
            throw new InvalidOperationException($"{Name} is already running.");
        }
        s_appStarted = true;

        var rootCommand = new RootCommand(Name);
        AddCommandLineOptions(rootCommand);
        rootCommand.SetHandler<TOptions>(RunAsync, new TBinder());
        return rootCommand.InvokeAsync(args);
    }

    protected void OnStartup(TOptions options)
    {
        Log($"Starting {Name}");
        Log($"Options:");
        Log($"{options}");

        ValidateOptions(options);

        RegisterSimpleMetric("env/processorcount", "Processor Count", "n0");
        LogMetric("env/processorcount", Environment.ProcessorCount);
    }
}

internal abstract class BaseClient<TOptions> : BenchmarkApp<TOptions>
    where TOptions : IBaseClientOptions
{
    private static bool s_isRunning;
    private static bool s_isWarmup;
    private static readonly TaskCompletionSource s_warmupCompletedTcs = new();

    protected static bool IsRunning => s_isRunning;
    protected static bool IsWarmup => s_isWarmup;
    protected static void OnWarmupCompleted() => s_warmupCompletedTcs.TrySetResult();

    protected abstract Task RunScenarioAsync(TOptions options);

    protected override async Task RunAsync(TOptions options)
    {
        OnStartup(options);

        s_isRunning = true;
        s_isWarmup = true;

        var scenarioTask = RunScenarioAsync(options);

        Log($"Warmup {options.Warmup.TotalSeconds}s");
        await Task.Delay(options.Warmup).ConfigureAwait(false);
        s_isWarmup = false;
        Log("Completing...");

        await s_warmupCompletedTcs.Task.WaitAsync(options.Warmup).ConfigureAwait(false); // warmup timeout = 2 * warmup duration
        Log("Done");

        Log($"Scenario {options.Duration.TotalSeconds}s");
        await Task.Delay(options.Duration).ConfigureAwait(false);
        s_isRunning = false;
        Log("Completing...");

        await scenarioTask.ConfigureAwait(false);
    }
}
