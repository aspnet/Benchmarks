// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Net.Quic;
using System.Net.Security;

namespace System.Net.Benchmarks.Tls.QuicBenchmark;

internal class QuicServerConnection(QuicConnection _connection, TlsBenchmarkServerOptions _serverOptions) : ITlsBenchmarkServerConnection
{
    private static readonly byte[] s_byteBuf = new byte[1];

    public Task CompleteHandshakeAsync(CancellationToken _) => Task.CompletedTask;
    public SslApplicationProtocol NegotiatedApplicationProtocol => _connection.NegotiatedApplicationProtocol;
    public bool IsMultiplexed => true;

    public async Task<Stream> AcceptInboundStreamAsync(CancellationToken cancellationToken)
    {
        var stream = await _connection.AcceptInboundStreamAsync(cancellationToken);
        if (_serverOptions.ReceiveBufferSize == 0)
        {
            // drain the single byte used to open the stream
            _ = await stream.ReadAsync(s_byteBuf, cancellationToken).ConfigureAwait(false);
        }
        return stream;
    }

    public ValueTask DisposeAsync() => _connection.DisposeAsync();
}
