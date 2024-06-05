using System.Net.Sockets;

namespace System.Net.Benchmarks.NetworkStreamBenchmark.Client;

internal class NetworkStreamClientConnection : SingleStreamConnection<NetworkStream>, IConnection
{
    public NetworkStreamClientConnection(NetworkStream innerStream) => InnerStream = innerStream;
    public ValueTask<NetworkStream> EstablishStreamAsync(NetworkStreamClientOptions options) => ValueTask.FromResult(ConsumeStream());
}
