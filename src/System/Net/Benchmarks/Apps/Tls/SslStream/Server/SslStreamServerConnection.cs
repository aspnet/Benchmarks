// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Net.Security;
using System.Net.Sockets;

namespace System.Net.Benchmarks.Tls.SslStreamBenchmark;

internal class SslStreamServerConnection(Socket _socket, SslServerAuthenticationOptions _sslOptions) : ITlsBenchmarkServerConnection
{
    private SslStream? _sslStream;
    private bool _streamConsumed;

    public bool IsMultiplexed => false;
    public SslApplicationProtocol NegotiatedApplicationProtocol
        => _sslStream?.NegotiatedApplicationProtocol ?? throw new InvalidOperationException("Handshake not completed");

    public async Task CompleteHandshakeAsync(CancellationToken cancellationToken)
    {
        if (_sslStream is not null)
        {
            return;
        }
        var networkStream = new NetworkStream(_socket, ownsSocket: true);
        _sslStream = new SslStream(networkStream, leaveInnerStreamOpen: false);
        await _sslStream.AuthenticateAsServerAsync(_sslOptions, cancellationToken).ConfigureAwait(false);
    }

    public Task<Stream> AcceptInboundStreamAsync(CancellationToken cancellationToken)
    {
        if (_sslStream is null)
        {
            throw new InvalidOperationException("Handshake not completed");
        }

        if (_streamConsumed)
        {
            throw new InvalidOperationException("Stream already consumed");
        }
        _streamConsumed = true;

        return Task.FromResult<Stream>(_sslStream!);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
