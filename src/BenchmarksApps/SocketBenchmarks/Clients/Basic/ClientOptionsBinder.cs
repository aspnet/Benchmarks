namespace SocketBenchmarks.Clients.Basic;
internal class ClientOptionsBinder : BinderBase<ClientOptions>
{
    public static Option<string> AddressOption { get; } = new("--address", () => "127.0.0.1", "The address to connect to");
    public static Option<int> PortOption { get; } = new("--port", () => 5678, "The port to connect to");
    public static Option<int> MessageSizeOption { get; } = new("--message-size", () => 1024, "The size of the message to send");
    public static Option<int> ConnectionsOption { get; } = new("--connections", () => 128, "The number of connections to make");
    public static Option<int> DurationOption { get; } = new("--duration", () => 0, "The duration time as second of the test");
    public static Option<int> WarmupTimeOption { get; } = new("--warmup-time", () => 5, "The warmup time as second");
    public static Option<int> TimeoutOption { get; } = new("--timeout", () => 0, "The timeout time as second for the test");
    public static Option<int> ReportingIntervalOption { get; } = new("--reporting-interval", () => 3, "The reporting interval time as second");

    protected override ClientOptions GetBoundValue(BindingContext bindingContext)
    {
        var parsed = bindingContext.ParseResult;

        if (!IPAddress.TryParse(parsed.GetValueForOption(AddressOption), out var ip))
        {
            throw new ArgumentException("Invalid IP address");
        }

        return new ClientOptions
        {
            Address = ip,
            Port = parsed.GetValueForOption(PortOption),
            MessageSize = parsed.GetValueForOption(MessageSizeOption),
            Connections = parsed.GetValueForOption(ConnectionsOption),
            Duration = parsed.GetValueForOption(DurationOption),
            WarmupTime = parsed.GetValueForOption(WarmupTimeOption),
            Timeout = parsed.GetValueForOption(TimeoutOption),
            ReportingInterval = parsed.GetValueForOption(ReportingIntervalOption)
        };
    }
}