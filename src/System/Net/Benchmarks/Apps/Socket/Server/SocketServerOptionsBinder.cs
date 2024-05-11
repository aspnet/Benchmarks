using System.CommandLine;
using System.CommandLine.Parsing;
using System.Net.Benchmarks.SocketBenchmark.Shared;

namespace System.Net.Benchmarks.SocketBenchmark;

internal class SocketServerOptionsBinder : BenchmarkOptionsBinder<SocketServerOptions>
{
    public static Option<int> ListenBacklogOption {get; } = new Option<int>("--listen-backlog", () => -1, "The server's maximum length of the pending connections queue.");
    public override void AddCommandLineArguments(RootCommand command)
    {
        SocketOptionsHelper.AddOptions(command);
        command.AddOption(ListenBacklogOption);
    }

    protected override void BindOptions(SocketServerOptions options, ParseResult parsed)
    {
        SocketOptionsHelper.BindOptions(options, parsed);
        options.ListenBacklog = parsed.GetValueForOption(ListenBacklogOption);
    }
}
