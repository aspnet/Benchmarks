using System.CommandLine.Parsing;

namespace SocketBenchmarks.Clients.Basic;
internal class ClientOptionsBinder : BinderBase<ClientOptions>
{
    public static Option<string> EndPointOption { get; } = new("--endpoint", () => "127.0.0.1:5678", "The endpoint to connect to");
    public static Option<int> ReceiveBufferSizeOption { get; } = new("--receive-buffer-size", () => 32768, "The size of the message to receive");
    public static Option<int> SendBufferSizeOption { get; } = new("--send-buffer-size", () => 32768, "The size of the message to send");
    public static Option<int> ConnectionsOption { get; } = new("--connections", () => 1, "The number of connections to make");
    public static Option<int> DurationOption { get; } = new("--duration", () => 15, "The duration time as second of the test");
    public static Option<int> WarmupTimeOption { get; } = new("--warmup-time", () => 15, "The warmup time as second");
    public static Option<string> ScenarioOption { get; } = new("--scenario", () => "ReadWrite", "The scenario to run");

    public static void AddOptions(RootCommand cmd)
    {
        cmd.Add(EndPointOption);
        cmd.Add(ReceiveBufferSizeOption);
        cmd.Add(SendBufferSizeOption);
        cmd.Add(ConnectionsOption);
        cmd.Add(DurationOption);
        cmd.Add(WarmupTimeOption);
        cmd.Add(ScenarioOption);
    }

    protected override ClientOptions GetBoundValue(BindingContext bindingContext)
    {
        var parsed = bindingContext.ParseResult;

        if (!parsed.HasOption(EndPointOption) || !IPEndPoint.TryParse(parsed.GetValueForOption(EndPointOption)!, out var endPoint))
        {
            throw new ArgumentException("Invalid endpoint");
        }

        if (!Enum.TryParse<Scenario>(parsed.GetValueForOption(ScenarioOption), true, out var scenario))
        {
            throw new ArgumentException("Invalid scenario");
        }

        int connections = parsed.GetValueForOption(ConnectionsOption);
        if (connections <= 0)
        {
            throw new ArgumentException("Connections should be greater than 0");
        }

        return new()
        {
            EndPoint = endPoint,
            ReceiveBufferSize = parsed.GetValueForOption(ReceiveBufferSizeOption),
            SendBufferSize = parsed.GetValueForOption(SendBufferSizeOption),
            Connections = connections,
            Duration = TimeSpan.FromSeconds(parsed.GetValueForOption(DurationOption)),
            WarmupTime = TimeSpan.FromSeconds(parsed.GetValueForOption(WarmupTimeOption)),
            Scenario = scenario,
        };
    }
}