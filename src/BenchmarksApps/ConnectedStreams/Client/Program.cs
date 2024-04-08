using System.Diagnostics;
using System.Net.Security;
using System.CommandLine;
using Microsoft.Crank.EventSources;

using ConnectedStreams.Shared;
using System.CommandLine.Binding;

namespace ConnectedStreams.Client;

// avoid false sharing among counters
internal class Metrics
{
    public double BytesReadPerSecond;
    public double BytesWrittenPerSecond;
}

internal interface IClientConnection : IAsyncDisposable
{
    Task<Stream> EstablishStreamAsync(ClientOptions options);
}

internal abstract class BenchmarkClient<TConnectionOptions, TOptions> where TOptions : ClientOptions
{
    private static bool _isInit;
    private static bool _isRunning;
    private static bool _isWarmup;

    public abstract string Name { get; }
    public abstract string MetricPrefix { get; }
    public abstract void AddCommandLineOptions(RootCommand command);
    public abstract void ValidateOptions(TOptions options);
    public abstract TConnectionOptions CreateClientConnectionOptions(TOptions options);
    public abstract Task<IClientConnection> EstablishConnectionAsync(TConnectionOptions connectionOptions, TOptions options);

    public Task RunCommandAsync<TBinder>(string[] args)
        where TBinder : BinderBase<TOptions>, new()
    {
        if (_isInit)
        {
            throw new InvalidOperationException("Client is already runnung.");
        }
        _isInit = true;

        var rootCommand = new RootCommand(Name);
        AddCommandLineOptions(rootCommand);
        rootCommand.SetHandler<TOptions>(RunAsync, new TBinder());
        return rootCommand.InvokeAsync(args);
    }

    private async Task RunAsync(TOptions options)
    {
        ValidateOptions(options);

        BenchmarksEventSource.Register("env/processorcount", Operations.First, Operations.First, "Processor Count", "Processor Count", "n0");
        LogMetric("env/processorcount", Environment.ProcessorCount);

        _isRunning = true;
        _isWarmup = true;

        var connectionOptions = CreateClientConnectionOptions(options);

        var scenarioTask = options.Scenario switch
        {
            Scenario.Handshake => RunHandshakeScenario(connectionOptions, options),
            Scenario.ReadWrite => RunReadWriteScenario(connectionOptions, options),
            Scenario.Rps => RunRpsScenario(connectionOptions, options),
            _ => throw new InvalidOperationException($"Unknown scenario: {options.Scenario}")
        };

        await Task.Delay(options.Warmup).ConfigureAwait(false);
        _isWarmup = false;
        Log("Completing warmup...");

        await Task.Delay(options.Duration).ConfigureAwait(false);
        _isRunning = false;
        Log("Completing scenario...");

        await scenarioTask.ConfigureAwait(false);
    }

    private async Task RunHandshakeScenario(TConnectionOptions connectionOptions, TOptions options)
    {
        RegisterPercentiledMetric($"{MetricPrefix}/handshake", "Handshake duration (ms)", "Handshakes duration in milliseconds");

        var tasks = new List<Task<List<double>>>(options.Connections);
        for (var i = 0; i < options.Connections; i++)
        {
            tasks.Add(HandshakeScenario(this, connectionOptions, options));
        }

        var metrics = await Task.WhenAll(tasks).ConfigureAwait(false);
        LogPercentiledMetric($"{MetricPrefix}/handshake", metrics.SelectMany(x => x).ToList());
    }

    private static async Task<List<double>> HandshakeScenario(BenchmarkClient<TConnectionOptions, TOptions> client, TConnectionOptions connectionOptions, TOptions options)
    {
        var values = new List<double>((int)options.Duration.TotalMilliseconds);
        var isWarmup = true;
        var sw = Stopwatch.StartNew();
        while (_isRunning)
        {
            sw.Restart();
            await using var connection = await client.EstablishConnectionAsync(connectionOptions, options).ConfigureAwait(false);
            var elapsedMs = sw.ElapsedTicks * 1000.0 / Stopwatch.Frequency;
            if (isWarmup && !_isWarmup)
            {
                isWarmup = false;
                values.Clear();
            }
            values.Add(elapsedMs);
        }
        return values;
    }

    private async Task RunReadWriteScenario(TConnectionOptions connectionOptions, TOptions options)
    {
        BenchmarksEventSource.Register($"{MetricPrefix}/write/mean", Operations.Avg, Operations.Avg, "Mean bytes written per second.", "Bytes per second - mean", "n2");
        BenchmarksEventSource.Register($"{MetricPrefix}/read/mean", Operations.Avg, Operations.Avg, "Mean bytes read per second.", "Bytes per second - mean", "n2");

        var connections = new IClientConnection[options.Connections];
        var tasks = new List<Task<Metrics>>(options.Connections * options.Streams);
        for (var i = 0; i < options.Connections; i++)
        {
            connections[i] = await EstablishConnectionAsync(connectionOptions, options).ConfigureAwait(false);
            for (var j = 0; j < options.Streams; j++)
            {
                tasks.Add(ReadWriteScenario(connections[i], options));
            }
        }

        var metrics = await Task.WhenAll(tasks).ConfigureAwait(false);
        LogMetric($"{MetricPrefix}/read/mean", metrics.Sum(x => x.BytesReadPerSecond));
        LogMetric($"{MetricPrefix}/write/mean", metrics.Sum(x => x.BytesWrittenPerSecond));

        foreach (var connection in connections)
        {
            await connection.DisposeAsync();
        }
    }

    private async Task RunRpsScenario(TConnectionOptions connectionOptions, TOptions options)
    {
        BenchmarksEventSource.Register($"{MetricPrefix}/rps/mean", Operations.Avg, Operations.Avg, "Mean RPS", "RPS - mean", "n2");
        BenchmarksEventSource.Register($"{MetricPrefix}/errors", Operations.Sum, Operations.Sum, "Errors", "Errors", "n2");

        var connections = new IClientConnection[options.Connections];
        var tasks = new List<Task<(double Rps, long Errors)>>(options.Connections * options.Streams);
        for (var i = 0; i < options.Connections; i++)
        {
            connections[i] = await EstablishConnectionAsync(connectionOptions, options).ConfigureAwait(false);
            for (var j = 0; j < options.Streams; j++)
            {
                tasks.Add(RpsScenario(connections[i], options));
            }
        }

        var metrics = await Task.WhenAll(tasks).ConfigureAwait(false);
        LogMetric($"{MetricPrefix}/rps/mean", metrics.Sum(x => x.Rps));
        LogMetric($"{MetricPrefix}/errors", metrics.Sum(x => x.Errors));

        foreach (var connection in connections)
        {
            await connection.DisposeAsync();
        }
    }

    private static async Task<Metrics> ReadWriteScenario(IClientConnection connection, ClientOptions options)
    {
        await using var s = await connection.EstablishStreamAsync(options).ConfigureAwait(false);

        // spawn the reading and writing tasks on the thread pool, note that if we were to use
        //     var writeTask = WritingTask(stream, options.SendBufferSize);
        // then it could end up writing a lot of data until it finally suspended control back to this function.
        var writeTask = Task.Run(() => WritingTask(s, options.SendBufferSize));
        var readTask = Task.Run(() => ReadingTask(s, options.ReceiveBufferSize));

        await Task.WhenAll(writeTask, readTask).ConfigureAwait(false);

        return new Metrics
        {
            BytesReadPerSecond = await readTask,
            BytesWrittenPerSecond = await writeTask
        };

        async Task<double> WritingTask(Stream stream, int bufferSize)
        {
            if (bufferSize == 0)
            {
                return 0;
            }

            var sendBuffer = new byte[bufferSize];
            var sw = Stopwatch.StartNew();
            var isWarmup = true;

            long bytesWritten = 0;

            while (_isRunning)
            {
                if (isWarmup && !_isWarmup)
                {
                    isWarmup = false;
                    bytesWritten = 0;
                    sw.Restart();
                }

                await stream.WriteAsync(sendBuffer, default).ConfigureAwait(false);
                bytesWritten += bufferSize;
            }

            sw.Stop();
            var elapsed = sw.ElapsedTicks * 1.0 / Stopwatch.Frequency;
            return bytesWritten / elapsed;
        }

        async Task<double> ReadingTask(Stream stream, int bufferSize)
        {
            if (bufferSize == 0)
            {
                return 0;
            }

            var recvBuffer = new byte[bufferSize];
            var sw = Stopwatch.StartNew();
            var isWarmup = true;

            long bytesRead = 0;

            while (_isRunning)
            {
                if (isWarmup && !_isWarmup)
                {
                    isWarmup = false;
                    sw.Restart();
                    bytesRead = 0;
                }

                bytesRead += await stream.ReadAsync(recvBuffer, default).ConfigureAwait(false);
            }

            sw.Stop();
            var elapsed = sw.ElapsedTicks * 1.0 / Stopwatch.Frequency;
            return bytesRead / elapsed;
        }
    }

    private static async Task<(double Rps, long Errors)> RpsScenario(IClientConnection connection, ClientOptions options)
    {
        await using var stream = await connection.EstablishStreamAsync(options).ConfigureAwait(false);

        var sendBuffer = new byte[options.SendBufferSize];
        var receiveBuffer = new byte[options.ReceiveBufferSize];

        long successRequests = 0;
        long exceptionRequests = 0;

        var isWarmup = true;
        var sw = Stopwatch.StartNew();

        while (_isRunning)
        {
            if (isWarmup && !_isWarmup)
            {
                if (successRequests == 0)
                {
                    throw new Exception($"No successful requests during warmup.");
                }
                isWarmup = false;
                successRequests = 0;
                exceptionRequests = 0;
                sw.Restart();
            }

            try
            {
                await stream.WriteAsync(sendBuffer, default).ConfigureAwait(false);
                await stream.ReadExactlyAsync(receiveBuffer, 0, receiveBuffer.Length, default).ConfigureAwait(false);
                successRequests++;
            }
            catch (Exception ex)
            {
                Log("Exception: " + ex);
                exceptionRequests++;
            }
        }
        var elapsed = sw.ElapsedTicks * 1.0 / Stopwatch.Frequency;
        return (successRequests / elapsed, exceptionRequests);
    }

    protected static SslClientAuthenticationOptions CreateSslClientAuthenticationOptions(ClientOptions options)
    {
        var sslOptions = new SslClientAuthenticationOptions
        {
            TargetHost = options.TlsHostName ?? options.Hostname,
            RemoteCertificateValidationCallback = delegate { return true; },
            ApplicationProtocols = [
                options.Scenario switch {
                    Scenario.ReadWrite => ApplicationProtocolConstants.ReadWrite,
                    Scenario.Handshake => ApplicationProtocolConstants.Handshake,
                    Scenario.Rps => ApplicationProtocolConstants.Rps,
                    _ => throw new Exception("Unknown scenario")
                }
            ],
            CertificateRevocationCheckMode = options.CertificateRevocationCheckMode,
        };

        if (options.ClientCertificate != null)
        {
            switch (options.CertificateSelection)
            {
                case CertificateSelectionType.Collection:
                    sslOptions.ClientCertificates = [ options.ClientCertificate ];
                    break;
                case CertificateSelectionType.Callback:
                    sslOptions.LocalCertificateSelectionCallback = delegate { return options.ClientCertificate; };
                    break;
#if NET8_0_OR_GREATER
                case CertificateSelectionType.CertContext:
                    sslOptions.ClientCertificateContext = SslStreamCertificateContext.Create(options.ClientCertificate, []);
                    break;
#endif
                default:
                    throw new InvalidOperationException($"Certificate selection type {options.CertificateSelection} is not supported in this .NET version.");
            }
        }

        return sslOptions;
    }

    private static void RegisterPercentiledMetric(string name, string shortDescription, string longDescription)
    {
        BenchmarksEventSource.Register(name + "/avg", Operations.Min, Operations.Min, shortDescription + " - avg", longDescription + " - avg", "n3");
        BenchmarksEventSource.Register(name + "/min", Operations.Min, Operations.Min, shortDescription + " - min", longDescription + " - min", "n3");
        BenchmarksEventSource.Register(name + "/p50", Operations.Max, Operations.Max, shortDescription + " - p50", longDescription + " - 50th percentile", "n3");
        BenchmarksEventSource.Register(name + "/p75", Operations.Max, Operations.Max, shortDescription + " - p75", longDescription + " - 75th percentile", "n3");
        BenchmarksEventSource.Register(name + "/p90", Operations.Max, Operations.Max, shortDescription + " - p90", longDescription + " - 90th percentile", "n3");
        BenchmarksEventSource.Register(name + "/p99", Operations.Max, Operations.Max, shortDescription + " - p99", longDescription + " - 99th percentile", "n3");
        BenchmarksEventSource.Register(name + "/max", Operations.Max, Operations.Max, shortDescription + " - max", longDescription + " - max", "n3");
    }

    private static void LogPercentiledMetric(string name, List<double> values)
    {
        values.Sort();

        LogMetric(name + "/avg", values.Average());
        LogMetric(name + "/min", GetPercentile(0, values));
        LogMetric(name + "/p50", GetPercentile(50, values));
        LogMetric(name + "/p75", GetPercentile(75, values));
        LogMetric(name + "/p90", GetPercentile(90, values));
        LogMetric(name + "/p99", GetPercentile(99, values));
        LogMetric(name + "/max", GetPercentile(100, values));
    }

    private static double GetPercentile(int percent, List<double> sortedValues)
    {
        if (percent == 0)
        {
            return sortedValues[0];
        }

        if (percent == 100)
        {
            return sortedValues[^1];
        }

        var i = percent * sortedValues.Count / 100.0 + 0.5;
        var fractionPart = i - Math.Truncate(i);

        return (1.0 - fractionPart) * sortedValues[(int)Math.Truncate(i) - 1] + fractionPart * sortedValues[(int)Math.Ceiling(i) - 1];
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
