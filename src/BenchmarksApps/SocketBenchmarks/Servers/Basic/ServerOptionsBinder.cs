using System.CommandLine.Parsing;

namespace SocketBenchmarks.Servers.Basic;

internal class ServerOptionsBinder : BinderBase<ServerOptions>
{
    public static Option<int> PortOption { get; set; } = new("--port", () => 5678, "The address to listen to");
    public static Option<int> ReceiveBufferSizeOption { get; } = new("--receive-buffer-size", () => 32768, "The size of the message to receive");
    public static Option<int> SendBufferSizeOption { get; } = new("--send-buffer-size", () => 32768, "The size of the message to send");
    public static Option<int> MaxThreadCountOption { get; } = new("--max-thread-count", () => 0, "The maximum number of threads to use");
    public static Option<string> ScenarioOption { get; } = new("--scenario", "The scenario to run");

    public static void AddOptions(RootCommand cmd)
    {
        cmd.Add(PortOption);
        cmd.Add(ReceiveBufferSizeOption);
        cmd.Add(SendBufferSizeOption);
        cmd.Add(MaxThreadCountOption);
        cmd.Add(ScenarioOption);
    }

    protected override ServerOptions GetBoundValue(BindingContext bindingContext)
    {
        var parsed = bindingContext.ParseResult;

        int port = parsed.GetValueForOption(PortOption);
        if (port < 0 || port > ushort.MaxValue)
        {
            throw new ArgumentException("Port should be between 0 and 65535");
        }

        if (!Enum.TryParse<Scenario>(parsed.GetValueForOption(ScenarioOption), true, out var scenario))
        {
            throw new ArgumentException("Invalid scenario");
        }

        return new()
        {
            Port = port,
            ReceiveBufferSize = parsed.GetValueForOption(ReceiveBufferSizeOption),
            SendBufferSize = parsed.GetValueForOption(SendBufferSizeOption),
            MaxThreadCount = parsed.GetValueForOption(MaxThreadCountOption),
            Scenario = scenario,
        };
    }
}
