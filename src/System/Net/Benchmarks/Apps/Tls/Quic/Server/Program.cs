// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Net;
using System.Net.Quic;
using System.Net.Security;

using System.Net.Benchmarks;
using System.Net.Security.Benchmarks;

await QuicBenchmarkServer.RunAsync(args);

// ----------------------------

#pragma warning disable CA1416 // "This call site is reachable on all platforms. It is only supported on: 'linux', 'macOS/OSX', 'windows'."

internal class QuicBenchmarkServer : TlsBenchmarkServer<Listener, Connection, TlsBenchmarkServerOptions>
{
    public static Task RunAsync(string[] args)
        => new QuicBenchmarkServer().RunAsync<TlsBenchmarkServerOptionsBinder<TlsBenchmarkServerOptions>>(args);

    protected override string Name => "QUIC benchmark server";
    protected override string MetricPrefix => "quic";

    protected override bool IsExpectedException(Exception e)
        => e is QuicException qe && qe.QuicError == QuicError.ConnectionAborted;

    protected override async Task<Listener> ListenAsync(TlsBenchmarkServerOptions options, CancellationToken ct)
    {
        var sslOptions = CreateSslServerAuthenticationOptions(options);
        var connectionOptions = new QuicServerConnectionOptions()
        {
            DefaultStreamErrorCode = 123,
            DefaultCloseErrorCode = 456,
            ServerAuthenticationOptions = sslOptions,
            MaxInboundBidirectionalStreams = 20000
        };
        var listenerOptions = new QuicListenerOptions()
        {
            ListenEndPoint = new IPEndPoint(IPAddress.IPv6Any, options.Port),
            ApplicationProtocols = connectionOptions.ServerAuthenticationOptions.ApplicationProtocols!,
            ConnectionOptionsCallback = (_, _, _) => ValueTask.FromResult(connectionOptions)
        };

        return new Listener(await QuicListener.ListenAsync(listenerOptions, ct), options);
    }
}

internal class Listener(QuicListener _listener, TlsBenchmarkServerOptions _serverOptions) : IListener<Connection>
{
    public EndPoint LocalEndPoint => _listener.LocalEndPoint;

    public async Task<Connection> AcceptAsync(CancellationToken cancellationToken)
        => new Connection(await _listener.AcceptConnectionAsync(cancellationToken), _serverOptions);

    public ValueTask DisposeAsync() => _listener.DisposeAsync();
}

internal class Connection(QuicConnection _connection, TlsBenchmarkServerOptions _serverOptions) : ITlsBenchmarkServerConnection
{
    public Task CompleteHandshakeAsync(CancellationToken _) => Task.CompletedTask;
    public SslApplicationProtocol NegotiatedApplicationProtocol => _connection.NegotiatedApplicationProtocol;
    public bool IsMultiplexed => true;
    private static readonly byte[] s_byteBuf = new byte[1];

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

#pragma warning restore CA1416
