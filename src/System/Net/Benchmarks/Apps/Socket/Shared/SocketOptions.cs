namespace System.Net.Benchmarks.SocketBenchmark.Shared;

internal class SocketOptions
{
    public int ReceiveBufferSize { get; set; }
    public int SendBufferSize { get; set; }
    public int Port { get; set; }
    public Scenario Scenario { get; set; }


    public override string ToString() => $"ReceiveBufferSize: {ReceiveBufferSize}, SendBufferSize: {SendBufferSize}, Port: {Port}, Scenario: {Scenario}";
}