namespace System.Net.Benchmarks.NetworkStreamBenchmark.Shared;

internal class NetworkStreamOptions
{
    public int ReceiveBufferSize { get; set; }
    public int SendBufferSize { get; set; }
    public int Port { get; set; }
    public Scenario Scenario { get; set; }


    public override string ToString() => $"ReceiveBufferSize: {ReceiveBufferSize}, SendBufferSize: {SendBufferSize}, Port: {Port}, Scenario: {Scenario}";
}