using System;
using System.Net;
using System.Net.Security;
using System.Security.Authentication;
using System.Net.Sockets;
using System.CommandLine;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Crank.EventSources;

using SslStreamServer;

internal class Program
{
    private static int s_errors;

    private static async Task<int> Main(string[] args)
    {
        var rootCommand = new RootCommand("SslStream benchmark server");
        OptionsBinder.AddOptions(rootCommand);
        rootCommand.SetHandler<ServerOptions>(Run, new OptionsBinder());
        return await rootCommand.InvokeAsync(args).ConfigureAwait(false);
    }

    static async Task Run(ServerOptions options)
    {
        SetupMeasurements();

        var sslOptions = CreateSslServerAuthenticationOptions(options);
        using var sock = new Socket(SocketType.Stream, ProtocolType.Tcp);
        sock.Bind(new IPEndPoint(IPAddress.IPv6Any, options.Port));
        sock.Listen();

        Log($"Listening on {sock.LocalEndPoint}.");

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
                var clientSock = await sock.AcceptAsync(ctrlC.Token).ConfigureAwait(false);
                _ = Task.Run(() => ProcessClient(clientSock, options, sslOptions, ctrlC.Token));
            }
        }
        catch (OperationCanceledException)
        {
            // Expected, just return
        }

        LogMetric("sslstream/error", s_errors);
        Log("Exitting...");
    }

    static async Task ReadWriteScenario(SslStream stream, ServerOptions options, CancellationToken cancellationToken)
    {
        static async Task WritingTask(SslStream stream, int bufferSize, CancellationToken cancellationToken)
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

        static async Task ReadingTask(SslStream stream, int bufferSize, CancellationTokenSource cts)
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
    }

    static async Task ProcessClient(Socket socket, ServerOptions options, SslServerAuthenticationOptions sslOptions, CancellationToken cancellationToken)
    {
        try
        {
            using var stream = await EstablishSslStreamAsync(socket, sslOptions, cancellationToken).ConfigureAwait(false);

            if (stream.NegotiatedApplicationProtocol.Protocol.Span.SequenceEqual("Handshake"u8))
            {
                // done everything
                return;
            }
            if (stream.NegotiatedApplicationProtocol.Protocol.Span.SequenceEqual("ReadWrite"u8))
            {
                await ReadWriteScenario(stream, options, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                Log($"Negotiated unknown protocol: {stream.NegotiatedApplicationProtocol}");
                s_errors++;
            }
        }
        catch (AuthenticationException e)
        {
            Log($"Authentication failed: {e}");
            s_errors++;
        }
        catch (IOException e) when (e.InnerException is SocketException)
        {
            // client closed the connection, ignore this
        }
        catch (Exception e)
        {
            Console.WriteLine($"Exception occured: {e}");
        }
    }

    static SslServerAuthenticationOptions CreateSslServerAuthenticationOptions(ServerOptions options)
    {
        var sslOptions = new SslServerAuthenticationOptions
        {
            ClientCertificateRequired = options.RequireClientCertificate,
            RemoteCertificateValidationCallback = delegate { return true; },
            ApplicationProtocols = options.ApplicationProtocols,
#if NET8_0_OR_GREATER
            AllowTlsResume = options.AllowTlsResume,
#endif
            EnabledSslProtocols = options.EnabledSslProtocols,
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

    static async Task<SslStream> EstablishSslStreamAsync(Socket socket, SslServerAuthenticationOptions options, CancellationToken cancellationToken)
    {
        var networkStream = new NetworkStream(socket, ownsSocket: true);
        var stream = new SslStream(networkStream, leaveInnerStreamOpen: false);
        await stream.AuthenticateAsServerAsync(options, cancellationToken).ConfigureAwait(false);
        return stream;
    }

    public static void SetupMeasurements()
    {
        BenchmarksEventSource.Register("sslstream/error", Operations.First, Operations.First, "Connection error count", "Connection error count", "n0");
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