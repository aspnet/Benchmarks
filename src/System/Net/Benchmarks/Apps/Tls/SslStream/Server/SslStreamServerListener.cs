// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Net.Security;
using System.Net.Sockets;

namespace System.Net.Benchmarks.Tls.SslStreamBenchmark;

internal class SslStreamServerListener(Socket _listenSocket, SslServerAuthenticationOptions _sslOptions) : IListener<SslStreamServerConnection>
{
    public EndPoint LocalEndPoint => _listenSocket.LocalEndPoint!;

    public async Task<SslStreamServerConnection> AcceptAsync(CancellationToken cancellationToken)
    {
        var acceptSocket = await _listenSocket.AcceptAsync(cancellationToken).ConfigureAwait(false);
        return new SslStreamServerConnection(acceptSocket, _sslOptions);
    }

    public ValueTask DisposeAsync()
    {
        _listenSocket.Dispose();
        return default;
    }
}
