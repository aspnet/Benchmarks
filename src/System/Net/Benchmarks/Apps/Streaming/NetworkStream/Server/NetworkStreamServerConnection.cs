using System.Net.Sockets;
using System.Net.Benchmarks.NetworkStreamBenchmark.Shared;

namespace System.Net.Benchmarks.NetworkStreamBenchmark;

internal class NetworkStreamServerConnection : SingleStreamConnection<NetworkStream>, IConnection
{
    internal NetworkStreamServerConnection(NetworkStream innerStream) : base(innerStream)
    { }

    public ValueTask<NetworkStream> EstablishStreamAsync() => ValueTask.FromResult(ConsumeStream());
}
