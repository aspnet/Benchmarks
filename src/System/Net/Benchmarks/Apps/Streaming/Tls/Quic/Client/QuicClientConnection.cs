// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Net.Quic;

namespace System.Net.Benchmarks.Tls.QuicBenchmark;

internal class QuicClientConnection(QuicConnection _connection) : ITlsBenchmarkClientConnection
{
    private static readonly byte[] s_byteBuf = [ 42 ];

    public bool IsMultiplexed => true;

    public async Task<Stream> EstablishStreamAsync(TlsBenchmarkClientOptions options)
    {
        var stream = await _connection.OpenOutboundStreamAsync(QuicStreamType.Bidirectional);
        if (options.SendBufferSize == 0) // we must send at least one byte to open the stream on the wire
        {
            await stream.WriteAsync(s_byteBuf).ConfigureAwait(false);
        }
        return stream;
    }

    public ValueTask DisposeAsync() => _connection.DisposeAsync();
}
