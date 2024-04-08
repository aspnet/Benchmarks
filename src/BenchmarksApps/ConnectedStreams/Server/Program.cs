using System.Net;
using System.Net.Security;
using System.CommandLine;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Crank.EventSources;

using ConnectedStreams.Shared;
using System.CommandLine.Binding;

namespace ConnectedStreams.Server;

internal interface IListener : IAsyncDisposable
{
    EndPoint LocalEndPoint { get; }
    Task<IServerConnection> AcceptConnectionAsync(CancellationToken cancellationToken);
}

internal interface IServerConnection : IAsyncDisposable
{
    SslApplicationProtocol NegotiatedApplicationProtocol { get; }
    Task CompleteHandshakeAsync(CancellationToken cancellationToken);
    Task<Stream> AcceptInboundStreamAsync(CancellationToken cancellationToken);
}

internal abstract class BenchmarkServer<TOptions> where TOptions : ServerOptions
{
    private static int _errors;

    public abstract string Name { get; }
    public abstract string MetricPrefix { get; }
    public abstract void AddCommandLineOptions(RootCommand command);
    public abstract Task<IListener> ListenAsync(TOptions options);
    public abstract bool IsConnectionCloseException(Exception e);

    public Task RunCommandAsync<TBinder>(string[] args) where TBinder : BinderBase<TOptions>, new()
    {
        var rootCommand = new RootCommand(Name);
        AddCommandLineOptions(rootCommand);
        rootCommand.SetHandler<TOptions>(RunAsync, new TBinder());
        return rootCommand.InvokeAsync(args);
    }

    private async Task RunAsync(TOptions options)
    {
        BenchmarksEventSource.Register($"{MetricPrefix}/errors", Operations.First, Operations.First, "Connection error count", "Connection error count", "n0");
        LogMetric("env/processorcount", Environment.ProcessorCount);

        await using var listener = await ListenAsync(options);
        Log($"Listening on {listener.LocalEndPoint}.");

        var ctrlC = new CancellationTokenSource();
        Console.CancelKeyPress += (s, e) =>
        {
            Log("Shutting down...");
            e.Cancel = true;
            ctrlC.Cancel();
        };

        try
        {
            while (!ctrlC.IsCancellationRequested)
            {
                var connection = await listener.AcceptConnectionAsync(ctrlC.Token).ConfigureAwait(false);
                _ = Task.Run(() => ProcessClient(connection, options, ctrlC.Token));
            }
        }
        catch (OperationCanceledException)
        {
            // Expected, just return
        }

        LogMetric($"{MetricPrefix}/errors", _errors);
        Log("Exiting...");
    }

    private async Task ProcessClient(IServerConnection connection, ServerOptions options, CancellationToken ct)
    {
        try
        {
            await connection.CompleteHandshakeAsync(ct).ConfigureAwait(false);
            var alpn = connection.NegotiatedApplicationProtocol;

            if (alpn == ApplicationProtocolConstants.Handshake)
            {
                return; // All done
            }

            if (alpn == ApplicationProtocolConstants.ReadWrite)
            {
                await AcceptStreamsAsync(connection, options, ReadWriteScenario, ct).ConfigureAwait(false);
                return;
            }

            if (alpn == ApplicationProtocolConstants.Rps)
            {
                await AcceptStreamsAsync(connection, options, RpsScenario, ct).ConfigureAwait(false);
                return;
            }

            throw new Exception($"Negotiated unknown protocol: {connection.NegotiatedApplicationProtocol}");
        }
        catch (Exception e) when (IsConnectionCloseException(e))
        {
            // client closed the connection on us, this is expected as clients
            // simply close the connection after the test is done.
        }
        catch (Exception e)
        {
            Interlocked.Increment(ref _errors);
            Log($"Exception occured: {e}");
        }
        finally
        {
            await connection.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static async Task AcceptStreamsAsync(IServerConnection connection, ServerOptions options, Func<Stream, ServerOptions, CancellationToken, Task> scenario, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var stream = await connection.AcceptInboundStreamAsync(cancellationToken).ConfigureAwait(false);
            if (stream == null)
            {
                return;
            }
            _ = Task.Run(async () =>
                {
                    try
                    {
                        await scenario(stream, options, cancellationToken).ConfigureAwait(false);
                    }
                    finally
                    {
                        stream.Dispose();
                    }
                });
        }
    }

    private static async Task RpsScenario(Stream stream, ServerOptions options, CancellationToken token)
    {
        var sendBuffer = new byte[options.SendBufferSize];
        var recvBuffer = new byte[options.ReceiveBufferSize];

        int totalRead = 0;
        while (true)
        {
            var bytesRead = await stream.ReadAsync(recvBuffer, token).ConfigureAwait(false);
            if (bytesRead == 0)
            {
                if (totalRead > 0)
                {
                    throw new Exception("Unexpected EOF");
                }

                // client closed the connection.
                return;
            }

            totalRead += bytesRead;
            if (totalRead > options.ReceiveBufferSize)
            {
                throw new Exception("Unexpected data received");
            }

            if (totalRead == options.ReceiveBufferSize) // finished reading request
            {
                await stream.WriteAsync(sendBuffer, token).ConfigureAwait(false);
                totalRead = 0;
            }
        }
    }

    private static async Task ReadWriteScenario(Stream stream, ServerOptions options, CancellationToken ct)
    {
        static async Task WritingTask(Stream stream, int bufferSize, CancellationToken linkedCt)
        {
            if (bufferSize == 0)
            {
                return;
            }

            var sendBuffer = new byte[bufferSize];

            try
            {
                while (!linkedCt.IsCancellationRequested)
                {
                    await stream.WriteAsync(sendBuffer, linkedCt).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                // Expected, just return
            }
        }

        static async Task ReadingTask(Stream stream, int bufferSize, CancellationTokenSource linkedCts)
        {
            var recvBuffer = new byte[Math.Max(bufferSize, 1)];

            while (true)
            {
                var bytesRead = await stream.ReadAsync(recvBuffer, linkedCts.Token).ConfigureAwait(false);

                if (bytesRead > 0 && bufferSize == 0)
                {
                    throw new Exception("Client is sending data but the server is not expecting any");
                }

                if (bytesRead == 0)
                {
                    // client closed the connection.
                    linkedCts.Cancel();
                    break;
                }
            }
        }

        byte[] recvBuffer = new byte[options.ReceiveBufferSize];

        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var writeTask = Task.Run(() => WritingTask(stream, options.SendBufferSize, cts.Token), ct);
        var readTask = Task.Run(() => ReadingTask(stream, options.ReceiveBufferSize, cts), ct);

        await Task.WhenAll(writeTask, readTask).ConfigureAwait(false);
    }

    protected static SslServerAuthenticationOptions CreateSslServerAuthenticationOptions(ServerOptions options)
    {
        var sslOptions = new SslServerAuthenticationOptions
        {
            ClientCertificateRequired = options.RequireClientCertificate,
            RemoteCertificateValidationCallback = delegate { return true; },
            ApplicationProtocols = options.ApplicationProtocols,
            CertificateRevocationCheckMode = options.CertificateRevocationCheckMode,
        };

        switch (options.CertificateSelection)
        {
            case ServerCertSelectionType.Certificate:
                sslOptions.ServerCertificate = options.ServerCertificate;
                break;
            case ServerCertSelectionType.Callback:
                sslOptions.ServerCertificateSelectionCallback = delegate { return options.ServerCertificate; };
                break;
            case ServerCertSelectionType.CertContext:
                sslOptions.ServerCertificateContext = SslStreamCertificateContext.Create(options.ServerCertificate, new X509Certificate2Collection());
                break;
        }

        return sslOptions;
    }

    private static void Log(string message)
    {
        var time = DateTime.UtcNow.ToString("hh:mm:ss.fff");
        Console.WriteLine($"[{time}] {message}");
    }

    private static void LogMetric(string name, double value)
    {
        BenchmarksEventSource.Measure(name, value);
        Log($"{name}: {value}");
    }
}
