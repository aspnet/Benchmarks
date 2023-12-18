using System;
using System.Diagnostics;
using System.Net;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Net.Sockets;
using System.CommandLine;
using Microsoft.Crank.EventSources;
using SslStreamCommon;
using SslStreamClient;

internal class Program
{
    private static long s_bytesRead;
    private static long s_bytesWritten;
    private static long s_totalHandshakes;
    private static bool s_isRunning;

    private static async Task<int> Main(string[] args)
    {
        var rootCommand = new RootCommand("SslStream benchmark client");
        OptionsBinder.AddOptions(rootCommand);
        rootCommand.SetHandler<ClientOptions>(Run, new OptionsBinder());
        return await rootCommand.InvokeAsync(args);
    }

    static async Task Run(ClientOptions options)
    {
        SetupMeasurements();

        try
        {
            switch (options.Scenario)
            {
                case Scenario.ReadWrite:
                    await RunReadWriteScenario(options);
                    break;
                case Scenario.Handshake:
                    await RunHandshakeScenario(options);
                    break;
                default:
                    throw new InvalidOperationException($"Unknown scenario: {options.Scenario}");
            }
        }
        catch (Exception e)
        {
            Log($"Exception occured: {e}");
        }
    }

    static async Task RunHandshakeScenario(ClientOptions options)
    {
        BenchmarksEventSource.Register("sslstream/handshakes/mean", Operations.Avg, Operations.Avg, "Mean TLS handshakes per second.", "Handshakes per second - mean", "n0");

        static async Task EstablishConnection(ClientOptions options)
        {
            using var sock = new Socket(SocketType.Stream, ProtocolType.Tcp);
            await sock.ConnectAsync(options.Hostname, options.Port);
            using var stream = await EstablishSslStreamAsync(sock, options);

            Interlocked.Increment(ref s_totalHandshakes);
        }

        s_isRunning = true;

        var task = Task.Run(async () =>
        {
            while (s_isRunning)
            {
                await EstablishConnection(options);
            }
        });

        await Task.Delay(options.Warmup);
        Log("Completing warmup...");

        Stopwatch sw = Stopwatch.StartNew();
        Volatile.Write(ref s_totalHandshakes, 0);

        await Task.Delay(options.Duration);
        s_isRunning = false;
        Log("Completing scenario...");

        await task;
        sw.Stop();

        LogMetric("sslstream/handshakes/mean", s_totalHandshakes / sw.Elapsed.TotalSeconds);
    }

    static async Task RunReadWriteScenario(ClientOptions options)
    {
        BenchmarksEventSource.Register("sslstream/read/mean", Operations.Avg, Operations.Avg, "Mean bytes read per second.", "Bytes per second - mean", "n0");
        BenchmarksEventSource.Register("sslstream/write/mean", Operations.Avg, Operations.Avg, "Mean bytes written per second.", "Bytes per second - mean", "n0");

        static async Task WritingTask(SslStream stream, int bufferSize, CancellationToken cancellationToken)
        {
            if (bufferSize == 0)
            {
                return;
            }

            var sendBuffer = new byte[bufferSize];
            try
            {
                while (s_isRunning)
                {
                    await stream.WriteAsync(sendBuffer, cancellationToken);
                    Interlocked.Add(ref s_bytesWritten, bufferSize);
                }
            }
            catch (OperationCanceledException)
            {
                // Expected, just return
            }
        }

        static async Task ReadingTask(SslStream stream, int bufferSize, CancellationToken cancellationToken)
        {
            if (bufferSize == 0)
            {
                return;
            }

            var recvBuffer = new byte[bufferSize];
            try
            {
                while (s_isRunning)
                {
                    int bytesRead = await stream.ReadAsync(recvBuffer, cancellationToken);
                    Interlocked.Add(ref s_bytesRead, bytesRead);
                }
            }
            catch (OperationCanceledException)
            {
                // Expected, just return
            }
        }

        using var sock = new Socket(SocketType.Stream, ProtocolType.Tcp);
        await sock.ConnectAsync(options.Hostname, options.Port);
        using var stream = await EstablishSslStreamAsync(sock, options);

        byte[] recvBuffer = new byte[options.ReceiveBufferSize];

        CancellationTokenSource cts = new CancellationTokenSource();

        s_isRunning = true;

        var writeTask = Task.Run(() => WritingTask(stream, options.SendBufferSize, cts.Token));
        var readTask = Task.Run(() => ReadingTask(stream, options.ReceiveBufferSize, cts.Token));

        await Task.Delay(options.Warmup);
        Log("Completing warmup...");

        Stopwatch sw = Stopwatch.StartNew();
        Volatile.Write(ref s_bytesRead, 0);
        Volatile.Write(ref s_bytesWritten, 0);

        await Task.Delay(options.Duration);
        s_isRunning = false;
        Log("Completing scenario...");

        cts.Cancel();

        await writeTask;
        await readTask;
        sw.Stop();

        LogMetric("sslstream/read/mean", s_bytesRead / sw.Elapsed.TotalSeconds);
        LogMetric("sslstream/write/mean", s_bytesWritten / sw.Elapsed.TotalSeconds);
    }

    static SslClientAuthenticationOptions CreateSslClientAuthenticationOptions(ClientOptions options)
    {
        var sslOptions = new SslClientAuthenticationOptions
        {
            TargetHost = options.TlsHostName ?? options.Hostname,
            RemoteCertificateValidationCallback = delegate { return true; },
            ApplicationProtocols = new List<SslApplicationProtocol> { new(options.Scenario.ToString()) },
        };

        if (options.ClientCertificate != null)
        {
            switch (options.CertificateSource)
            {
                case CertificateSource.Certificate:
                    sslOptions.ClientCertificates = new X509CertificateCollection { options.ClientCertificate };
                    break;
                case CertificateSource.Callback:
                    sslOptions.LocalCertificateSelectionCallback = delegate { return options.ClientCertificate; };
                    break;
#if HAS_CLIENT_CERTIFICATE_CONTEXT
                case CertificateSource.Context:
                    sslOptions.ClientCertificateContext = SslStreamCertificateContext.Create(options.ClientCertificate, new X509Certificate2Collection());
                    break;
#endif
                default:
                    throw new InvalidOperationException($"Unsupported certificate source: {options.CertificateSource}");
            }
        }

        return sslOptions;
    }

    static async Task<SslStream> EstablishSslStreamAsync(Socket socket, ClientOptions options)
    {
        var networkStream = new NetworkStream(socket, ownsSocket: true);
        var stream = new SslStream(networkStream, leaveInnerStreamOpen: false);
        await stream.AuthenticateAsClientAsync(CreateSslClientAuthenticationOptions(options));
        return stream;
    }

    public static void SetupMeasurements()
    {
        BenchmarksEventSource.Register("env/processorcount", Operations.First, Operations.First, "Processor Count", "Processor Count", "n0");
        LogMetric("env/processorcount", Environment.ProcessorCount);
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