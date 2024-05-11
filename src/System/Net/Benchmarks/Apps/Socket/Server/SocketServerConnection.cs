using System.Net.Sockets;
using System.Net.Benchmarks.SocketBenchmark.Shared;

namespace System.Net.Benchmarks.SocketBenchmark;

internal class SocketServerConnection : SingleStreamConnection<NetworkStream>, IConnection
{
    internal SocketServerConnection(NetworkStream innerStream) : base(innerStream)
    { }

    public ValueTask<NetworkStream> EstablishStreamAsync() => ValueTask.FromResult(ConsumeStream());
}
