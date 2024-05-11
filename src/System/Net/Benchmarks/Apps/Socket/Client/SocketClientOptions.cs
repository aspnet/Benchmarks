using System.Net.Benchmarks.SocketBenchmark.Shared;

namespace System.Net.Benchmarks.SocketBenchmark.Client;

internal class SocketClientOptions : SocketOptions, IBenchmarkClientOptions
{
    public TimeSpan Warmup { get; set; }

    public TimeSpan Duration { get; set; }
    public int Connections { get; set; }
    public IPAddress? Address { get; set; }

    public override string ToString() => $"{base.ToString()}, Warmup: {Warmup}, Duration: {Duration}, Connections: {Connections}, Address: {Address}";
}
