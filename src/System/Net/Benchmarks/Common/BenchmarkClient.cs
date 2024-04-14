// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

namespace System.Net.Benchmarks;

internal interface IBenchmarkClientOptions
{
    TimeSpan Warmup { get; }
    TimeSpan Duration { get; }
}

internal abstract class BenchmarkClient<TOptions> : BenchmarkApp<TOptions>
    where TOptions : IBenchmarkClientOptions, new()
{
    private static bool s_isRunning;
    private static bool s_isWarmup;
    private static readonly TaskCompletionSource s_warmupCompletedTcs = new();

    protected static bool IsRunning => s_isRunning;
    protected static bool IsWarmup => s_isWarmup;
    protected static void OnWarmupCompleted() => s_warmupCompletedTcs.TrySetResult();

    protected abstract Task RunScenarioAsync(TOptions options, CancellationToken cancellationToken);

    protected override async Task RunBenchmarkAsync(TOptions options, CancellationToken cancellationToken)
    {
        s_isRunning = true;
        s_isWarmup = true;

        var scenarioTask = RunScenarioAsync(options, cancellationToken);

        var timeout = 3 * (options.Warmup + options.Duration);
        GlobalCts.CancelAfter(timeout);

        Log($"Warmup {options.Warmup.TotalSeconds}s");
        await Task.Delay(options.Warmup, cancellationToken).ConfigureAwait(false);
        s_isWarmup = false;
        Log("Completing...");
        await WaitForWarmupCompletion(cancellationToken).ConfigureAwait(false);

        Log($"Scenario {options.Duration.TotalSeconds}s");
        await Task.Delay(options.Duration, cancellationToken).ConfigureAwait(false);
        s_isRunning = false;
        Log("Completing...");

        await scenarioTask.ConfigureAwait(false);
    }

    private static async Task WaitForWarmupCompletion(CancellationToken cancellationToken)
    {
        using var warmupCtReg = cancellationToken.UnsafeRegister(static (_, ct) =>
        {
            s_warmupCompletedTcs.TrySetCanceled(ct);
        }, null);

        await s_warmupCompletedTcs.Task.ConfigureAwait(false);
        Log("Done");
    }
}
