namespace SocketBenchmarks.Servers.Basic;

internal class ServerOptions
{
    public required int Port { get; set; }
    public required int ReceiveBufferSize { get; set; }
    public required int SendBufferSize { get; set; }
    public required int MaxThreadCount { get; set; }
    public required Scenario Scenario { get; set; }
}
