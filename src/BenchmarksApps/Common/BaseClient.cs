using System.CommandLine;
using System.CommandLine.Binding;

using static Common.Logger;

namespace Common;

internal interface IBaseClientOptions
{
    TimeSpan Warmup { get; }
    TimeSpan Duration { get; }
}

internal abstract class BaseClient<TOptions> where TOptions : IBaseClientOptions
{
    private static bool s_isInitialized;
    private static bool s_isRunning;
    private static bool s_isWarmup;
    private static readonly TaskCompletionSource s_warmupCompletedTcs = new();

    protected static bool IsRunning => s_isRunning;
    protected static bool IsWarmup => s_isWarmup;
    protected static void OnWarmupCompleted() => s_warmupCompletedTcs.TrySetResult();

    public abstract string Name { get; }
    public abstract string MetricPrefix { get; }
    public abstract void AddCommandLineOptions(RootCommand command);
    public abstract void ValidateOptions(TOptions options);
    protected abstract Task RunScenarioAsync(TOptions options);

    public Task RunCommandAsync<TBinder>(string[] args)
        where TBinder : BinderBase<TOptions>, new()
    {
        if (s_isInitialized)
        {
            throw new InvalidOperationException("Client is already running.");
        }
        s_isInitialized = true;

        var rootCommand = new RootCommand(Name);
        AddCommandLineOptions(rootCommand);
        rootCommand.SetHandler<TOptions>(RunAsync, new TBinder());
        return rootCommand.InvokeAsync(args);
    }

    protected async Task RunAsync(TOptions options)
    {
        Log($"Starting {Name}");
        Log($"Options:");
        Log($"{options}");

        ValidateOptions(options);

        RegisterSimpleMetric("env/processorcount", "Processor Count", "n0");
        LogMetric("env/processorcount", Environment.ProcessorCount);

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
