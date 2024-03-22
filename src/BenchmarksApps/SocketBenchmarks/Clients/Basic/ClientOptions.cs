namespace SocketBenchmarks.Clients.Basic;

internal class ClientOptions
{
    public IPAddress Address { get; set; } = IPAddress.Loopback;
    public int Port { get; set; } = 5678;
    public int MessageSize { get; set; } = 1024;
    public int Connections { get; set; } = 128;
    public int Duration { get; set; } = 0;
    public int WarmupTime { get; set; } = 5;
    public int Timeout { get; set; } = 0;
    public int ReportingInterval { get; set; } = 3;
}
