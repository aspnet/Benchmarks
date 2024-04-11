using System.CommandLine;
using System.Net.Security;
using System.Net.Sockets;

using ConnectedStreams.Client;
using SslStreamClient;

internal class Program
{
    private static async Task Main(string[] args)
    {
        await new SslStreamBenchmarkClient().RunCommandAsync<SslStreamOptionsBinder>(args);
    }
}

internal class SslStreamBenchmarkClient : SslBenchmarkClient<SslClientAuthenticationOptions, SslStreamClientOptions>
{
    public override string Name => "SslStream benchmark client";
    public override string MetricPrefix => "sslstream";
    public override void AddCommandLineOptions(RootCommand command) => SslStreamOptionsBinder.AddOptions(command);
    public override void ValidateOptions(SslStreamClientOptions options)
    {
        if (options.Streams != 1)
        {
            throw new ArgumentException("SslStream does not support multiple streams per connection.");
        }
    }

    public override SslClientAuthenticationOptions CreateClientConnectionOptions(SslStreamClientOptions options)
    {
        var authOptions = CreateSslClientAuthenticationOptions(options);
        authOptions.EnabledSslProtocols = options.EnabledSslProtocols;
#if NET8_0_OR_GREATER
        authOptions.AllowTlsResume = options.AllowTlsResume;
#endif
        return authOptions;
    }

    public override async Task<IClientConnection> EstablishConnectionAsync(SslClientAuthenticationOptions authOptions, SslStreamClientOptions options)
    {
        var sock = new Socket(SocketType.Stream, ProtocolType.Tcp);
        await sock.ConnectAsync(options.Hostname, options.Port).ConfigureAwait(false);

        var networkStream = new NetworkStream(sock, ownsSocket: true);
        var stream = new SslStream(networkStream, leaveInnerStreamOpen: false);
        await stream.AuthenticateAsClientAsync(authOptions).ConfigureAwait(false);

        return new SslStreamClientConnection(stream);
    }
}

internal class SslStreamClientConnection(SslStream _sslStream) : IClientConnection
{
    public Task<Stream> EstablishStreamAsync(ClientOptions options)
    {
        Stream stream = _sslStream;
        _sslStream = null!;
        return Task.FromResult(stream);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}