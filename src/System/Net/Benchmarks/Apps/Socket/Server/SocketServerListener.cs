using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace System.Net.Benchmarks.SocketBenchmark;

internal class SocketServerListener(Socket listenSocket) : IListener<SocketServerConnection>
{
    public EndPoint LocalEndPoint => listenSocket.LocalEndPoint!;

    public async Task<SocketServerConnection> AcceptAsync(CancellationToken cancellationToken)
    {
        var acceptedSocket = await listenSocket.AcceptAsync(cancellationToken).ConfigureAwait(false);
        acceptedSocket.NoDelay = true;
        return new SocketServerConnection(new NetworkStream(acceptedSocket, ownsSocket: true));
    }

    public ValueTask DisposeAsync()
    {
        listenSocket.Dispose();
        return ValueTask.CompletedTask;
    }
}
