using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.CommandLine;

using ConnectedStreams.Server;
using SslStreamServer;

internal class Program
{
    private static async Task Main(string[] args)
    {
        var server = new SslStreamBenchmarkServer();
        await server.RunCommandAsync<SslStreamOptionsBinder>(args);
    }
}

internal class SslStreamBenchmarkServer : BenchmarkServer<SslStreamServerOptions>
{
    public override string Name => "SslStream benchmark server";
    public override string MetricPrefix => "sslstream";

    public override void AddCommandLineOptions(RootCommand rootCommand)
        => SslStreamOptionsBinder.AddOptions(rootCommand);

    public override bool IsConnectionCloseException(Exception e)
        => e is IOException && e.InnerException is SocketException se && se.SocketErrorCode == SocketError.ConnectionReset;

    public override Task<IListener> ListenAsync(SslStreamServerOptions options)
    {
        var sslOptions = CreateSslServerAuthenticationOptions(options);
        sslOptions.EnabledSslProtocols = options.EnabledSslProtocols;
#if NET8_0_OR_GREATER
        sslOptions.AllowTlsResume = options.AllowTlsResume;
#endif

        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
        socket.Bind(new IPEndPoint(IPAddress.IPv6Any, options.Port));
        socket.Listen();

        var listener = new SslStreamServerListener(socket, sslOptions);
        return Task.FromResult<IListener>(listener);
    }
}

internal class SslStreamServerListener(Socket _listenSocket, SslServerAuthenticationOptions _sslOptions) : IListener
{
    public EndPoint LocalEndPoint => _listenSocket.LocalEndPoint!;

    public async Task<IServerConnection> AcceptConnectionAsync(CancellationToken cancellationToken)
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

internal class SslStreamServerConnection(Socket _socket, SslServerAuthenticationOptions _sslOptions) : IServerConnection
{
    private SslStream? _sslStream;
    private bool _streamConsumed;
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
            //Special-case for SslStream
            return Task.FromResult<Stream>(null!);
        }
        _streamConsumed = true;

        return Task.FromResult<Stream>(_sslStream!);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
