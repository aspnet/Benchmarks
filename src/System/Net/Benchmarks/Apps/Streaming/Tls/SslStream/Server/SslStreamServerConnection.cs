// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Net.Security;
using System.Net.Sockets;

namespace System.Net.Benchmarks.Tls.SslStreamBenchmark;

internal class SslStreamServerConnection(Socket _socket, SslServerAuthenticationOptions _sslOptions) : SingleStreamConnection<SslStream>, ITlsBenchmarkServerConnection
{
    public SslApplicationProtocol NegotiatedApplicationProtocol
        => InnerStream?.NegotiatedApplicationProtocol ?? throw new InvalidOperationException("Handshake not completed");

    public async Task CompleteHandshakeAsync(CancellationToken cancellationToken)
    {
        if (InnerStream is not null)
        {
            return;
        }
        var networkStream = new NetworkStream(_socket, ownsSocket: true);
        InnerStream = new SslStream(networkStream, leaveInnerStreamOpen: false);
        await InnerStream.AuthenticateAsServerAsync(_sslOptions, cancellationToken).ConfigureAwait(false);
    }

    public Task<Stream> AcceptInboundStreamAsync(CancellationToken _) => Task.FromResult<Stream>(ConsumeStream());
}
