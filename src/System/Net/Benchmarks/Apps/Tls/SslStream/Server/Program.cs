// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Net;
using System.Net.Security;
using System.Net.Sockets;

using System.Net.Benchmarks;
using System.Net.Security.Benchmarks;
using System.Net.Security.Benchmarks.SslStream;

internal class Program
{
    private static async Task Main(string[] args)
    {
        await SslStreamBenchmarkServer.RunAsync(args);
    }
}

internal class SslStreamBenchmarkServer : TlsBenchmarkServer<Listener, Connection, Options>
{
    public static Task RunAsync(string[] args)
        => new SslStreamBenchmarkServer().RunAsync<OptionsBinder>(args);

    protected override string Name => "SslStream benchmark server";
    protected override string MetricPrefix => "sslstream";

    protected override bool IsExpectedException(Exception e)
        => e is IOException && e.InnerException is SocketException se && se.SocketErrorCode == SocketError.ConnectionReset;

    protected override Task<Listener> ListenAsync(Options options, CancellationToken ct)
    {
        var sslOptions = CreateSslServerAuthenticationOptions(options);
        sslOptions.EnabledSslProtocols = options.EnabledSslProtocols;
#if NET8_0_OR_GREATER
        sslOptions.AllowTlsResume = options.AllowTlsResume;
#endif

        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
        socket.Bind(new IPEndPoint(IPAddress.IPv6Any, options.Port));
        socket.Listen();

        var listener = new Listener(socket, sslOptions);
        return Task.FromResult(listener);
    }
}

internal class Listener(Socket _listenSocket, SslServerAuthenticationOptions _sslOptions) : IListener<Connection>
{
    public EndPoint LocalEndPoint => _listenSocket.LocalEndPoint!;

    public async Task<Connection> AcceptAsync(CancellationToken cancellationToken)
    {
        var acceptSocket = await _listenSocket.AcceptAsync(cancellationToken).ConfigureAwait(false);
        return new Connection(acceptSocket, _sslOptions);
    }

    public ValueTask DisposeAsync()
    {
        _listenSocket.Dispose();
        return default;
    }
}

internal class Connection(Socket _socket, SslServerAuthenticationOptions _sslOptions) : ITlsBenchmarkServerConnection
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
            throw new InvalidOperationException("Handshake already completed");
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
