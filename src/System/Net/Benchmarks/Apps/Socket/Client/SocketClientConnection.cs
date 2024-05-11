using System.Net.Sockets;

namespace System.Net.Benchmarks.SocketBenchmark.Client;

internal class SocketClientConnection : SingleStreamConnection<NetworkStream>, IConnection
{
    public SocketClientConnection(NetworkStream innerStream) => InnerStream = innerStream;
    public ValueTask<NetworkStream> EstablishStreamAsync(SocketClientOptions options) => ValueTask.FromResult(ConsumeStream());
}
