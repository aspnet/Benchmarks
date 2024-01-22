using System;
using System.Runtime.InteropServices;
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

// avoid false sharing among counters
[StructLayout(LayoutKind.Explicit)]
internal struct Counters
{
    [FieldOffset(0)] public long BytesRead;
    [FieldOffset(64)] public long BytesWritten;
    [FieldOffset(128)] public long TotalHandshakes;
}

internal class Program
{
    private static Counters s_counters;
    private static bool s_isRunning;

    private static async Task<int> Main(string[] args)
    {
        var rootCommand = new RootCommand("SslStream benchmark client");
        OptionsBinder.AddOptions(rootCommand);
        rootCommand.SetHandler<ClientOptions>(Run, new OptionsBinder());
        return await rootCommand.InvokeAsync(args).ConfigureAwait(false);
    }

    static async Task Run(ClientOptions options)
    {
        SetupMeasurements();

        try
        {
            switch (options.Scenario)
            {
                case Scenario.ReadWrite:
                    await RunReadWriteScenario(options).ConfigureAwait(false);
                    break;
                case Scenario.Handshake:
                    await RunHandshakeScenario(options).ConfigureAwait(false);
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
        BenchmarksEventSource.Register("sslstream/handshake/mean", Operations.Avg, Operations.Avg, "Mean handshake duration (ms)", "Handshakes duration in milliseconds - mean", "n3");

        static async Task EstablishConnection(ClientOptions options)
        {
            using var sock = new Socket(SocketType.Stream, ProtocolType.Tcp);
            await sock.ConnectAsync(options.Hostname, options.Port).ConfigureAwait(false);
            using var stream = await EstablishSslStreamAsync(sock, options).ConfigureAwait(false);

            Interlocked.Increment(ref s_counters.TotalHandshakes);
        }

        s_isRunning = true;

        var task = Task.Run(async () =>
        {
            while (s_isRunning)
            {
                await EstablishConnection(options).ConfigureAwait(false);
            }
        });

        await Task.Delay(options.Warmup).ConfigureAwait(false);
        Log("Completing warmup...");

        Stopwatch sw = Stopwatch.StartNew();
        Volatile.Write(ref s_counters.TotalHandshakes, 0);

        await Task.Delay(options.Duration).ConfigureAwait(false);
        s_isRunning = false;
        Log("Completing scenario...");

        await task.ConfigureAwait(false);
        sw.Stop();

        LogMetric("sslstream/handshake/mean", sw.Elapsed.TotalMilliseconds / s_counters.TotalHandshakes);
    }

    static async Task RunReadWriteScenario(ClientOptions options)
    {
        BenchmarksEventSource.Register("sslstream/read/mean", Operations.Avg, Operations.Avg, "Mean bytes read per second.", "Bytes per second - mean", "n2");
        BenchmarksEventSource.Register("sslstream/write/mean", Operations.Avg, Operations.Avg, "Mean bytes written per second.", "Bytes per second - mean", "n2");

        static async Task WritingTask(SslStream stream, int bufferSize, CancellationToken cancellationToken = default)
        {
            if (bufferSize == 0)
            {
                return;
            }

            var sendBuffer = new byte[bufferSize];
            while (s_isRunning)
            {
                await stream.WriteAsync(sendBuffer, cancellationToken).ConfigureAwait(false);
                Interlocked.Add(ref s_counters.BytesWritten, bufferSize);
            }
        }

        static async Task ReadingTask(SslStream stream, int bufferSize, CancellationToken cancellationToken = default)
        {
            if (bufferSize == 0)
            {
                return;
            }

            var recvBuffer = new byte[bufferSize];
            while (s_isRunning)
            {
                int bytesRead = await stream.ReadAsync(recvBuffer, cancellationToken).ConfigureAwait(false);
                Interlocked.Add(ref s_counters.BytesRead, bytesRead);
            }
        }

        using var sock = new Socket(SocketType.Stream, ProtocolType.Tcp);
        await sock.ConnectAsync(options.Hostname, options.Port).ConfigureAwait(false);
        using var stream = await EstablishSslStreamAsync(sock, options).ConfigureAwait(false);

        byte[] recvBuffer = new byte[options.ReceiveBufferSize];

        s_isRunning = true;

        var writeTask = Task.Run(() => WritingTask(stream, options.SendBufferSize));
        var readTask = Task.Run(() => ReadingTask(stream, options.ReceiveBufferSize));

        await Task.Delay(options.Warmup).ConfigureAwait(false);
        Log("Completing warmup...");

        Stopwatch sw = Stopwatch.StartNew();
        Volatile.Write(ref s_counters.BytesRead, 0);
        Volatile.Write(ref s_counters.BytesWritten, 0);

        await Task.Delay(options.Duration).ConfigureAwait(false);
        s_isRunning = false;
        Log("Completing scenario...");

        await Task.WhenAll(writeTask, readTask).ConfigureAwait(false);
        sw.Stop();

        LogMetric("sslstream/read/mean", s_counters.BytesRead / sw.Elapsed.TotalSeconds);
        LogMetric("sslstream/write/mean", s_counters.BytesWritten / sw.Elapsed.TotalSeconds);
    }

    static SslClientAuthenticationOptions CreateSslClientAuthenticationOptions(ClientOptions options)
    {
        var sslOptions = new SslClientAuthenticationOptions
        {
            TargetHost = options.TlsHostName ?? options.Hostname,
            RemoteCertificateValidationCallback = delegate { return true; },
            ApplicationProtocols = new List<SslApplicationProtocol> { new(options.Scenario.ToString()) },
#if NET8_0_OR_GREATER
            AllowTlsResume = options.AllowTlsResume,
#endif
            EnabledSslProtocols = options.EnabledSslProtocols,
            CertificateRevocationCheckMode = options.CertificateRevocationCheckMode,
        };

        if (options.ClientCertificate != null)
        {
            switch (options.CertificateSelection)
            {
                case CertificateSelectionType.Collection:
                    sslOptions.ClientCertificates = new X509CertificateCollection { options.ClientCertificate };
                    break;
                case CertificateSelectionType.Callback:
                    sslOptions.LocalCertificateSelectionCallback = delegate { return options.ClientCertificate; };
                    break;
#if NET8_0_OR_GREATER
                case CertificateSelectionType.CertContext:
                    sslOptions.ClientCertificateContext = SslStreamCertificateContext.Create(options.ClientCertificate, new X509Certificate2Collection());
                    break;
#endif
                default:
                    throw new InvalidOperationException($"Certificate selection type {options.CertificateSelection} is not supported in this .NET version.");
            }
        }

        return sslOptions;
    }

    static async Task<SslStream> EstablishSslStreamAsync(Socket socket, ClientOptions options)
    {
        var networkStream = new NetworkStream(socket, ownsSocket: true);
        var stream = new SslStream(networkStream, leaveInnerStreamOpen: false);
        await stream.AuthenticateAsClientAsync(CreateSslClientAuthenticationOptions(options)).ConfigureAwait(false);
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