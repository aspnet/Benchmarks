namespace SocketBenchmarks.Clients.Basic;

public class ClientOptions
{
    public required IPEndPoint EndPoint { get; set; }
    public required int ReceiveBufferSize { get; set; }
    public required int SendBufferSize { get; set; }
    public required int Connections { get; set; }
    public required TimeSpan Duration { get; set; }
    public required TimeSpan WarmupTime { get; set; }
    public required Scenario Scenario { get; set; }
}
