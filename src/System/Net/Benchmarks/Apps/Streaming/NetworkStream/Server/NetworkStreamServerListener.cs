using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace System.Net.Benchmarks.NetworkStreamBenchmark;

internal class NetworkStreamServerListener(Socket listenSocket) : IListener<NetworkStreamServerConnection>
{
    public EndPoint LocalEndPoint => listenSocket.LocalEndPoint!;

    public async Task<NetworkStreamServerConnection> AcceptAsync(CancellationToken cancellationToken)
    {
        var acceptedSocket = await listenSocket.AcceptAsync(cancellationToken).ConfigureAwait(false);
        acceptedSocket.NoDelay = true;
        return new NetworkStreamServerConnection(new NetworkStream(acceptedSocket, ownsSocket: true));
    }

    public ValueTask DisposeAsync()
    {
        listenSocket.Dispose();
        return ValueTask.CompletedTask;
    }
}
