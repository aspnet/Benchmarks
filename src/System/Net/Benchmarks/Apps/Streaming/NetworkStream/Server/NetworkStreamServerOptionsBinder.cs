using System.CommandLine;
using System.CommandLine.Parsing;
using System.Net.Benchmarks.NetworkStreamBenchmark.Shared;

namespace System.Net.Benchmarks.NetworkStreamBenchmark;

internal class NetworkStreamServerOptionsBinder : BenchmarkOptionsBinder<NetworkStreamServerOptions>
{
    public static Option<int> ListenBacklogOption {get; } = new Option<int>("--listen-backlog", () => -1, "The server's maximum length of the pending connections queue.");
    public override void AddCommandLineArguments(RootCommand command)
    {
        NetworkStreamOptionsHelper.AddOptions(command);
        command.AddOption(ListenBacklogOption);
    }

    protected override void BindOptions(NetworkStreamServerOptions options, ParseResult parsed)
    {
        NetworkStreamOptionsHelper.BindOptions(options, parsed);
        options.ListenBacklog = parsed.GetValueForOption(ListenBacklogOption);
    }
}
