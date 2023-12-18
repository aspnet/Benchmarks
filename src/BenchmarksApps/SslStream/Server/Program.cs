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
    private static async Task<int> Main(string[] args)
    {
        var rootCommand = new RootCommand("SslStream benchmark server");
        OptionsBinder.AddOptions(rootCommand);
        rootCommand.SetHandler<ServerOptions>(Run, new OptionsBinder());
        return await rootCommand.InvokeAsync(args);
    }

    static async Task Run(ServerOptions options)
    {
        var sslOptions = CreateSslServerAuthenticationOptions(options);
        using var sock = new Socket(SocketType.Stream, ProtocolType.Tcp);
        sock.Bind(new IPEndPoint(IPAddress.IPv6Any, options.Port));
        sock.Listen();

        Log($"Listening on {sock.LocalEndPoint}.");

        CancellationTokenSource ctrlC = new CancellationTokenSource();
        Console.CancelKeyPress += (s, e) =>
        {
            Log("Ctrl+C pressed.");
            e.Cancel = true;
            ctrlC.Cancel();
        };

        try
        {
            while (!ctrlC.IsCancellationRequested)
            {
                var clientSock = await sock.AcceptAsync(ctrlC.Token);
                _ = Task.Run(() => ProcessClient(clientSock, options, sslOptions, ctrlC.Token));
            }
        }
        catch (OperationCanceledException)
        {
            // Expected, just return
        }

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
                    await stream.WriteAsync(sendBuffer, cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                // Expected, just return
            }
        }

        byte[] recvBuffer = new byte[options.ReceiveBufferSize];

        CancellationTokenSource cts = new CancellationTokenSource();
        using var reg = cancellationToken.Register(() => cts.Cancel());

        var writeTask = Task.Run(() => WritingTask(stream, options.SendBufferSize, cts.Token));

        var recvBufer = new byte[options.ReceiveBufferSize];

        while (true)
        {
            if (await stream.ReadAsync(recvBufer, cts.Token) == 0)
            {
                cts.Cancel();
                break;
            }
        }

        await writeTask;
    }

    static async Task ProcessClient(Socket socket, ServerOptions options, SslServerAuthenticationOptions sslOptions, CancellationToken cancellationToken)
    {
        try
        {
            using var stream = await EstablishSslStreamAsync(socket, sslOptions, cancellationToken);

            if (stream.NegotiatedApplicationProtocol.Protocol.Span.SequenceEqual("Handshake"u8))
            {
                // done everything
                return;
            }
            if (stream.NegotiatedApplicationProtocol.Protocol.Span.SequenceEqual("ReadWrite"u8))
            {
                await ReadWriteScenario(stream, options, cancellationToken);
            }
            else
            {
                Log($"Negotiated unknown protocol: {stream.NegotiatedApplicationProtocol}");
            }
        }
        catch (AuthenticationException e)
        {
            Log($"Authentication failed: {e}");
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
#if HAS_ALLOW_TLS_RESUME
            AllowTlsResume = !options.DisableTlsResume,
#endif
            EnabledSslProtocols = options.EnabledSslProtocols,
            CertificateRevocationCheckMode = options.CertificateRevocationCheckMode,
        };

        switch (options.CertificateSource)
        {
            case CertificateSource.Certificate:
                sslOptions.ServerCertificate = options.ServerCertificate;
                break;
            case CertificateSource.Callback:
                sslOptions.ServerCertificateSelectionCallback = delegate { return options.ServerCertificate; };
                break;
            case CertificateSource.Context:
                sslOptions.ServerCertificateContext = SslStreamCertificateContext.Create(options.ServerCertificate, new X509Certificate2Collection());
                break;
        }

        return sslOptions;
    }

    static async Task<SslStream> EstablishSslStreamAsync(Socket socket, SslServerAuthenticationOptions options, CancellationToken cancellationToken)
    {
        var networkStream = new NetworkStream(socket, ownsSocket: true);
        var stream = new SslStream(networkStream, leaveInnerStreamOpen: false);
        await stream.AuthenticateAsServerAsync(options, cancellationToken);
        return stream;
    }

    static void Log(string message)
    {
        var time = DateTime.UtcNow.ToString("hh:mm:ss.fff");
        Console.WriteLine($"[{time}] {message}");
    }
}