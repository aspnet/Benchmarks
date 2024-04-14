// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Diagnostics;

using System.Net.Benchmarks;

namespace System.Net.Security.Benchmarks;

internal record ReadWriteMetrics(double BytesReadPerSecond, double BytesWrittenPerSecond);

internal interface ITlsBenchmarkClientConnection : IAsyncDisposable
{
    Task<Stream> EstablishStreamAsync(TlsBenchmarkClientOptions options);
}

internal abstract class TlsBenchmarkClient<TConnection, TConnectionOptions, TOptions> : BenchmarkClient<TOptions>
    where TConnection : ITlsBenchmarkClientConnection
    where TOptions : TlsBenchmarkClientOptions, new()
{
    protected abstract TConnectionOptions CreateClientConnectionOptions(TOptions options);
    protected abstract Task<TConnection> EstablishConnectionAsync(TConnectionOptions connectionOptions, TOptions options);
    protected override Task RunScenarioAsync(TOptions options, CancellationToken cancellationToken)
    {
        var connectionOptions = CreateClientConnectionOptions(options);
        return options.Scenario switch
        {
            Scenario.Handshake => RunHandshakeScenario(connectionOptions, options),
            Scenario.ReadWrite => RunReadWriteScenario(connectionOptions, options),
            Scenario.Rps => RunRpsScenario(connectionOptions, options),
            _ => throw new InvalidOperationException($"Unknown scenario: {options.Scenario}")
        };
    }

    private async Task RunHandshakeScenario(TConnectionOptions connectionOptions, TOptions options)
    {
        RegisterPercentileMetric(MetricName.Handshake, "Handshake duration (ms)");

        var tasks = new List<Task<List<double>>>(options.Connections);
        for (var i = 0; i < options.Connections; i++)
        {
            tasks.Add(HandshakeScenario(EstablishConnectionAsync, connectionOptions, options));
        }

        var metrics = await Task.WhenAll(tasks).ConfigureAwait(false);
        LogPercentileMetric(MetricName.Handshake, metrics.SelectMany(x => x).ToList());
    }

    private async Task RunReadWriteScenario(TConnectionOptions connectionOptions, TOptions options)
    {
        RegisterMetric(MetricName.Read + MetricName.Mean, "Read B/s - mean");
        RegisterMetric(MetricName.Write + MetricName.Mean, "Write B/s - mean");

        var connections = new ITlsBenchmarkClientConnection[options.Connections];
        var tasks = new List<Task<ReadWriteMetrics>>(options.Connections * options.Streams);
        for (var i = 0; i < options.Connections; i++)
        {
            connections[i] = await EstablishConnectionAsync(connectionOptions, options).ConfigureAwait(false);
            for (var j = 0; j < options.Streams; j++)
            {
                tasks.Add(ReadWriteScenario(connections[i], options));
            }
        }

        var metrics = await Task.WhenAll(tasks).ConfigureAwait(false);
        LogMetric(MetricName.Read + MetricName.Mean, metrics.Sum(x => x.BytesReadPerSecond));
        LogMetric(MetricName.Write + MetricName.Mean, metrics.Sum(x => x.BytesWrittenPerSecond));

        foreach (var connection in connections)
        {
            await connection.DisposeAsync();
        }
    }

    private async Task RunRpsScenario(TConnectionOptions connectionOptions, TOptions options)
    {
        RegisterMetric(MetricName.Rps + MetricName.Mean, "RPS - mean");
        RegisterMetric(MetricName.Errors, "Errors", "n0");

        var connections = new ITlsBenchmarkClientConnection[options.Connections];
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
        LogMetric(MetricName.Rps + MetricName.Mean, metrics.Sum(x => x.Rps));
        if (metrics.Any(x => x.Errors > 0))
        {
            LogMetric(MetricName.Errors, metrics.Sum(x => x.Errors));
        }

        foreach (var connection in connections)
        {
            await connection.DisposeAsync();
        }
    }

    private static async Task<List<double>> HandshakeScenario(Func<TConnectionOptions, TOptions, Task<TConnection>> establishConnectionAsync, TConnectionOptions connectionOptions, TOptions options)
    {
        var values = new List<double>((int)options.Duration.TotalMilliseconds);
        var isWarmup = true;
        var sw = Stopwatch.StartNew();
        while (IsRunning)
        {
            if (isWarmup && !IsWarmup)
            {
                isWarmup = false;
                values.Clear();
                OnWarmupCompleted();
            }
            sw.Restart();
            await using var connection = await establishConnectionAsync(connectionOptions, options).ConfigureAwait(false);
            var elapsedMs = sw.ElapsedTicks * 1000.0 / Stopwatch.Frequency;
            values.Add(elapsedMs);
        }
        return values;
    }

    private static async Task<ReadWriteMetrics> ReadWriteScenario(ITlsBenchmarkClientConnection connection, TlsBenchmarkClientOptions options)
    {
        await using var s = await connection.EstablishStreamAsync(options).ConfigureAwait(false);

        // spawn the reading and writing tasks on the thread pool, note that if we were to use
        //     var writeTask = WritingTask(stream, options.SendBufferSize);
        // then it could end up writing a lot of data until it finally suspended control back to this function.
        var writeTask = Task.Run(() => WritingTask(s, options.SendBufferSize));
        var readTask = Task.Run(() => ReadingTask(s, options.ReceiveBufferSize));

        await Task.WhenAll(writeTask, readTask).ConfigureAwait(false);
        return new ReadWriteMetrics(BytesReadPerSecond: await readTask, BytesWrittenPerSecond: await writeTask);

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

            while (IsRunning)
            {
                if (isWarmup && !IsWarmup)
                {
                    isWarmup = false;
                    bytesWritten = 0;
                    OnWarmupCompleted();
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

            while (IsRunning)
            {
                if (isWarmup && !IsWarmup)
                {
                    if (bytesRead == 0)
                    {
                        throw new Exception($"No bytes read during warmup.");
                    }
                    isWarmup = false;
                    bytesRead = 0;
                    OnWarmupCompleted();
                    sw.Restart();
                }

                bytesRead += await stream.ReadAsync(recvBuffer, default).ConfigureAwait(false);
            }

            sw.Stop();
            var elapsed = sw.ElapsedTicks * 1.0 / Stopwatch.Frequency;
            return bytesRead / elapsed;
        }
    }

    private static async Task<(double Rps, long Errors)> RpsScenario(ITlsBenchmarkClientConnection connection, TlsBenchmarkClientOptions options)
    {
        await using var stream = await connection.EstablishStreamAsync(options).ConfigureAwait(false);

        var sendBuffer = new byte[options.SendBufferSize];
        var receiveBuffer = new byte[options.ReceiveBufferSize];

        long successRequests = 0;
        long exceptionRequests = 0;

        var isWarmup = true;
        var sw = Stopwatch.StartNew();

        while (IsRunning)
        {
            if (isWarmup && !IsWarmup)
            {
                if (successRequests == 0)
                {
                    throw new Exception($"No successful requests during warmup.");
                }
                isWarmup = false;
                successRequests = 0;
                exceptionRequests = 0;
                OnWarmupCompleted();
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

    protected static SslClientAuthenticationOptions CreateSslClientAuthenticationOptions(TlsBenchmarkClientOptions options)
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
}
