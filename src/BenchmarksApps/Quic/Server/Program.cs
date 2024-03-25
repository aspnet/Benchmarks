using System.Net;
using System.Net.Security;
using System.CommandLine;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Crank.EventSources;

using SslStreamServer;
using SslStreamCommon;
using System.Net.Quic;

#pragma warning disable CA1416 // This call site is reachable on all platforms. It is only supported on: 'linux', 'macOS/OSX', 'windows'.

internal class Program
{
    private static int s_errors;

    private static async Task<int> Main(string[] args)
    {
        var rootCommand = new RootCommand("QUIC benchmark server");
        OptionsBinder.AddOptions(rootCommand);
        rootCommand.SetHandler<ServerOptions>(Run, new OptionsBinder());
        return await rootCommand.InvokeAsync(args).ConfigureAwait(false);
    }

    static async Task Run(ServerOptions options)
    {
        SetupMeasurements();

        var sslOptions = CreateSslServerAuthenticationOptions(options);
        var connectionOptions = new QuicServerConnectionOptions()
        {
            DefaultStreamErrorCode = 123,
            DefaultCloseErrorCode = 456,
            ServerAuthenticationOptions = sslOptions,
            MaxInboundBidirectionalStreams = 50000
        };
        var listenerOptions = new QuicListenerOptions()
        {
            ListenEndPoint = new IPEndPoint(IPAddress.IPv6Any, options.Port),
            ApplicationProtocols = connectionOptions.ServerAuthenticationOptions.ApplicationProtocols!,
            ConnectionOptionsCallback = (_, _, _) => ValueTask.FromResult(connectionOptions)
        };
        await using var listener = await QuicListener.ListenAsync(listenerOptions);

        Log($"Listening on {listener.LocalEndPoint}.");

        CancellationTokenSource ctrlC = new CancellationTokenSource();
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
                var clientConn = await listener.AcceptConnectionAsync(ctrlC.Token).ConfigureAwait(false);
                _ = Task.Run(() => ProcessClient(clientConn, options, ctrlC.Token));
            }
        }
        catch (OperationCanceledException)
        {
            // Expected, just return
        }

        LogMetric("quic/errors", s_errors);
        Log("Exiting...");
    }

    static async Task ReadWriteScenario(QuicConnection quicConn, ServerOptions options, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var stream = await quicConn.AcceptInboundStreamAsync(cancellationToken);
            _ = Task.Run(() => ReadWriteScenario(stream, options, cancellationToken));
        }
    }

    static async Task ReadWriteScenario(Stream stream, ServerOptions options, CancellationToken cancellationToken)
    {
        static async Task WritingTask(Stream stream, int bufferSize, CancellationToken cancellationToken)
        {
            if (bufferSize == 0)
            {
                return;
            }

            var sendBuffer = new byte[bufferSize];

            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    await stream.WriteAsync(sendBuffer, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                // Expected, just return
            }
        }

        static async Task ReadingTask(Stream stream, int bufferSize, CancellationTokenSource cts)
        {
            var recvBuffer = new byte[Math.Max(bufferSize, 1)];

            while (true)
            {
                var bytesRead = await stream.ReadAsync(recvBuffer, cts.Token).ConfigureAwait(false);

                if (bytesRead > 0 && bufferSize == 0)
                {
                    throw new Exception("Client is sending data but the server is not expecting any");
                }

                if (bytesRead == 0)
                {
                    // client closed the connection.
                    cts.Cancel();
                    break;
                }
            }
        }

        byte[] recvBuffer = new byte[options.ReceiveBufferSize];

        CancellationTokenSource cts = new CancellationTokenSource();
        using var reg = cancellationToken.Register(() => cts.Cancel());

        var writeTask = Task.Run(() => WritingTask(stream, options.SendBufferSize, cts.Token));
        var readTask = Task.Run(() => ReadingTask(stream, options.ReceiveBufferSize, cts));

        await Task.WhenAll(writeTask, readTask).ConfigureAwait(false);
        stream.Dispose();
    }

    static async Task ProcessClient(QuicConnection clientConn, ServerOptions options, CancellationToken cancellationToken)
    {
        try
        {
            if (clientConn.NegotiatedApplicationProtocol == ApplicationProtocolConstants.Handshake)
            {
                // done everything
                return;
            }
            if (clientConn.NegotiatedApplicationProtocol == ApplicationProtocolConstants.ReadWrite)
            {
                await ReadWriteScenario(clientConn, options, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                throw new Exception($"Negotiated unknown protocol: {clientConn.NegotiatedApplicationProtocol}");
            }
        }
        catch (QuicException e) when (e.QuicError == QuicError.ConnectionAborted)
        {
            // client closed the connection on us, this is expected as clients
            // simply close the connection after the test is done.
        }
        catch (Exception e)
        {
            Interlocked.Increment(ref s_errors);
            Log($"Exception occured: {e}");
        }
    }

    static SslServerAuthenticationOptions CreateSslServerAuthenticationOptions(ServerOptions options)
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
            case CertificateSelectionType.Certificate:
                sslOptions.ServerCertificate = options.ServerCertificate;
                break;
            case CertificateSelectionType.Callback:
                sslOptions.ServerCertificateSelectionCallback = delegate { return options.ServerCertificate; };
                break;
            case CertificateSelectionType.CertContext:
                sslOptions.ServerCertificateContext = SslStreamCertificateContext.Create(options.ServerCertificate, new X509Certificate2Collection());
                break;
        }

        return sslOptions;
    }

    public static void SetupMeasurements()
    {
        BenchmarksEventSource.Register("quic/errors", Operations.First, Operations.First, "Connection error count", "Connection error count", "n0");
        LogMetric("env/processorcount", Environment.ProcessorCount);
    }

    static void Log(string message)
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