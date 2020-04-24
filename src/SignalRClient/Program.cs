// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using McMaster.Extensions.CommandLineUtils;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace SignalRClient
{
    class Program
    {
        private static List<HubConnection> _connections;
        private static List<int> _requestsPerConnection;
        private static List<List<double>> _latencyPerConnection;
        private static List<(double sum, int count)> _latencyAverage;
        private static List<IDisposable> _recvCallbacks;
        private static Stopwatch _workTimer = new Stopwatch();
        private static Timer _sendDelayTimer;
        private static bool _stopped;
        private static SemaphoreSlim _lock = new SemaphoreSlim(1);
        private static double _clientToServerOffset;
        private static int _totalRequests;
        private static HttpClientHandler _httpClientHandler;
        private static StringBuilder _errorStringBuilder = new StringBuilder();

        public static string ServerUrl { get; set; }
        public static string Scenario { get; set; }
        public static int WarmupTimeSeconds { get; set; }
        public static int ExecutionTimeSeconds { get; set; }
        public static int Connections { get; set; }
        public static bool CollectLatency { get; set; }
        public static LogLevel LogLevel { get; set; }
        public static string HubProtocol { get; set; }
        public static HttpTransportType TransportType { get; set; }
        public static TimeSpan SendDelay { get; set; }

        static async Task Main(string[] args)
        {
            var app = new CommandLineApplication();

            app.HelpOption("-h|--help");
            var optionUrl = app.Option("-u|--url <URL>", "The server url to request", CommandOptionType.SingleValue);
            var optionConnections = app.Option<int>("-c|--connections <N>", "Total number of SignalR connections to keep open", CommandOptionType.SingleValue);
            var optionWarmup = app.Option<int>("-w|--warmup <N>", "Duration of the warmup in seconds", CommandOptionType.SingleValue);
            var optionDuration = app.Option<int>("-d|--duration <N>", "Duration of the test in seconds", CommandOptionType.SingleValue);
            var optionScenario = app.Option("-s|--scenario <S>", "Scenario to run", CommandOptionType.SingleValue);
            var optionLatency = app.Option<bool>("-l|--latency <B>", "Whether to collect detailed latency", CommandOptionType.SingleOrNoValue);
            var optionProtocol = app.Option<string>("-p|--protocol <S>", "The SignalR protocol to use 'json', 'messagepack'", CommandOptionType.SingleValue);
            var optionLogLevel = app.Option<string>("-log|--loglevel <S>", "The log level to use for Console logging", CommandOptionType.SingleOrNoValue);
            var optionTransportType = app.Option<string>("-t|--transport <S>", "The Transport to use, e.g. WebSockets", CommandOptionType.SingleValue);
            var optionSendDelay = app.Option<int>("-sd|--sendDelay <S>", "The delay between sending messages for the 'echoIdle' scenario", CommandOptionType.SingleOrNoValue);

            app.OnExecuteAsync(cancellationToken =>
            {
                Console.WriteLine("SignalR Client");

                Console.WriteLine("#StartJobStatistics");
                Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new
                {
                    Metadata = new object[]
                    {
                        new { Source= "Benchmarks", Name= "SignalRClientVersion", ShortDescription = "ASP.NET Core SignalR Client Version", LongDescription = "ASP.NET Core SignalR Client Version" },
                    },
                    Measurements = new object[]
                    {
                        new { Timestamp = DateTime.UtcNow, Name = "SignalRClientVersion", Value = typeof(HubConnection).GetTypeInfo().Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion },
                    }
                }));
                Console.WriteLine("#EndJobStatistics");

                Scenario = optionScenario.Value();

                ServerUrl = optionUrl.Value();

                WarmupTimeSeconds = optionWarmup.HasValue()
                    ? int.Parse(optionWarmup.Value())
                    : 0;

                ExecutionTimeSeconds = int.Parse(optionDuration.Value());

                Connections = int.Parse(optionConnections.Value());

                CollectLatency = optionLatency.HasValue()
                    ? bool.Parse(optionLatency.Value())
                    : false;

                LogLevel = optionLogLevel.HasValue()
                    ? Enum.Parse<LogLevel>(optionLogLevel.Value(), ignoreCase: true)
                    : LogLevel.None;

                HubProtocol = optionProtocol.Value();

                TransportType = Enum.Parse<HttpTransportType>(optionTransportType.Value(), ignoreCase: true);

                SendDelay = optionSendDelay.HasValue()
                    ? TimeSpan.FromSeconds(int.Parse(optionSendDelay.Value()))
                    : TimeSpan.FromSeconds(60 * 10);

                return RunAsync();
            });

            await app.ExecuteAsync(args);
        }

        private static async Task RunAsync()
        {
            // Configuring the http client to trust the self-signed certificate
            _httpClientHandler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
            };

            CreateConnections();

            await StartScenario();

            await StopJobAsync();
        }

        private static async Task StartScenario()
        {
            var tasks = new List<Task>(_connections.Count);
            foreach (var connection in _connections)
            {
                tasks.Add(connection.StartAsync());
            }

            await Task.WhenAll(tasks);

            var cts = new CancellationTokenSource();
            var tcs = new TaskCompletionSource<object>();
            cts.Token.Register(() => tcs.SetResult(null));
            cts.CancelAfter(TimeSpan.FromSeconds(ExecutionTimeSeconds));
            _workTimer.Restart();

            try
            {
                switch (Scenario)
                {
                    case "broadcast":
                        await CalculateClientToServerOffset();
                        // SendAsync will return as soon as the request has been sent (non-blocking)
                        await _connections[0].SendAsync("Broadcast", ExecutionTimeSeconds + 1);
                        break;
                    case "echo":
                        for (var i = 0; i < _connections.Count; i++)
                        {
                            var id = i;
                            // kick off a task per connection so they don't wait for other connections when sending "Echo"
                            _ = Task.Run(async () =>
                            {
                                while (!cts.IsCancellationRequested)
                                {
                                    var time = await _connections[id].InvokeAsync<DateTime>("Echo", DateTime.UtcNow, cts.Token);

                                    ReceivedDateTime(time, id);
                                }
                            });
                        }
                        break;
                    case "echoAll":
                        while (!cts.IsCancellationRequested)
                        {
                            for (var i = 0; i < _connections.Count; i++)
                            {
                                _ = _connections[i].SendAsync("EchoAll", DateTime.UtcNow, cts.Token);
                            }
                        }
                        break;
                    case "echoIdle":
                        if (_sendDelayTimer == null)
                        {
                            _sendDelayTimer = new Timer(async (timer) =>
                            {
                                for (var id = 0; id < _connections.Count; id++)
                                {
                                    try
                                    {
                                        var time = await _connections[id].InvokeAsync<DateTime>("Echo", DateTime.UtcNow);
                                        ReceivedDateTime(time, id);
                                    }
                                    catch (Exception ex)
                                    {
                                        var text = "Error while invoking hub method " + ex.Message;
                                        Log(text);
                                    }
                                }
                            }, null, TimeSpan.Zero, SendDelay);
                        }
                        break;
                    default:
                        throw new Exception($"Scenario '{Scenario}' is not a known scenario.");
                }
            }
            catch (Exception ex)
            {
                var text = "Exception from test: " + ex.Message;
                Log(text);
                _errorStringBuilder.AppendLine();
                _errorStringBuilder.Append($"[{DateTime.Now.ToString("hh:mm:ss.fff")}] {text}");
            }

            await tcs.Task;
        }

        private static async Task StopJobAsync()
        {
            Log($"Stopping client.");
            if (_stopped || !await _lock.WaitAsync(0))
            {
                // someone else is stopping, we only need to do it once
                return;
            }
            try
            {
                _stopped = true;
                _workTimer.Stop();
                CalculateStatistics();
            }
            finally
            {
                _lock.Release();
            }

            BenchmarksEventSource.Log.Metadata("signalr/raw-errors", "all", "all", "Raw errors", "Raw errors", "object");
            BenchmarksEventSource.Measure("signalr/raw-errors", _errorStringBuilder.ToString());
        }

        private static void CreateConnections()
        {
            _connections = new List<HubConnection>(Connections);
            _requestsPerConnection = new List<int>(Connections);
            _latencyPerConnection = new List<List<double>>(Connections);
            _latencyAverage = new List<(double sum, int count)>(Connections);

            _recvCallbacks = new List<IDisposable>(Connections);
            for (var i = 0; i < Connections; i++)
            {
                var hubConnectionBuilder = new HubConnectionBuilder()
                .WithUrl(ServerUrl, httpConnectionOptions =>
                {
                    httpConnectionOptions.HttpMessageHandlerFactory = _ => _httpClientHandler;
                    httpConnectionOptions.Transports = TransportType;
                });

                if (LogLevel != LogLevel.None)
                {
                    hubConnectionBuilder.ConfigureLogging(builder =>
                    {
                        builder.AddConsole();
                        builder.SetMinimumLevel(LogLevel);
                    });
                }

                switch (HubProtocol)
                {
                    case "messagepack":
                        hubConnectionBuilder.AddMessagePackProtocol();
                        break;
                    case "json":
                        // json hub protocol is set by default
                        break;
                    default:
                        throw new Exception($"{HubProtocol} is an invalid hub protocol name.");
                }

                _requestsPerConnection.Add(0);
                _latencyPerConnection.Add(new List<double>());
                _latencyAverage.Add((0, 0));

                var connection = hubConnectionBuilder.Build();
                _connections.Add(connection);

                // Capture the connection ID
                var id = i;
                // setup event handlers
                _recvCallbacks.Add(connection.On<DateTime>("send", utcNow =>
                {
                    ReceivedDateTime(utcNow, id);
                }));

                connection.Closed += e =>
                {
                    if (!_stopped)
                    {
                        var error = $"Connection closed early: {e}";
                        _errorStringBuilder.AppendLine();
                        _errorStringBuilder.Append($"[{DateTime.Now.ToString("hh:mm:ss.fff")}] {error}");
                        Log(error);
                    }

                    return Task.CompletedTask;
                };
            }
        }

        private static void ReceivedDateTime(DateTime dateTime, int connectionId)
        {
            if (_stopped)
            {
                return;
            }

            _requestsPerConnection[connectionId] += 1;

            var latency = DateTime.UtcNow - dateTime;
            latency = latency.Add(TimeSpan.FromMilliseconds(_clientToServerOffset));
            if (CollectLatency)
            {
                _latencyPerConnection[connectionId].Add(latency.TotalMilliseconds);
            }
            else
            {
                var (sum, count) = _latencyAverage[connectionId];
                sum += latency.TotalMilliseconds;
                count++;
                _latencyAverage[connectionId] = (sum, count);
            }
        }

        private static void CalculateStatistics()
        {
            // RPS
            var requestDelta = 0;
            var newTotalRequests = 0;
            var min = int.MaxValue;
            var max = 0;
            for (var i = 0; i < _requestsPerConnection.Count; i++)
            {
                newTotalRequests += _requestsPerConnection[i];

                if (_requestsPerConnection[i] > max)
                {
                    max = _requestsPerConnection[i];
                }
                if (_requestsPerConnection[i] < min)
                {
                    min = _requestsPerConnection[i];
                }
            }

            requestDelta = newTotalRequests - _totalRequests;
            _totalRequests = newTotalRequests;

            // Review: This could be interesting information, see the gap between most active and least active connection
            // Ideally they should be within a couple percent of each other, but if they aren't something could be wrong
            Log($"Least Requests per Connection: {min}");
            Log($"Most Requests per Connection: {max}");

            if (_workTimer.ElapsedMilliseconds <= 0)
            {
                Log("Job failed to run");
                return;
            }

            var rps = (double)requestDelta / _workTimer.ElapsedMilliseconds * 1000;
            Log($"Total RPS: {rps}");

            BenchmarksEventSource.Log.Metadata("signalr/rps/max", "max", "sum", "Max RPS", "RPS: max", "n0");
            BenchmarksEventSource.Log.Metadata("signalr/requests", "max", "sum", "Requests", "Total number of requests", "n0");

            BenchmarksEventSource.Measure("signalr/rps/max", rps);
            BenchmarksEventSource.Measure("signalr/requests", requestDelta);

            // Latency
            CalculateLatency();
        }

        private static void CalculateLatency()
        {
            BenchmarksEventSource.Log.Metadata("signalr/latency/mean", "max", "sum", "Mean latency (ms)", "Mean latency (ms)", "n0");
            BenchmarksEventSource.Log.Metadata("signalr/latency/50", "max", "sum", "50th percentile latency (ms)", "50th percentile latency (ms)", "n0");
            BenchmarksEventSource.Log.Metadata("signalr/latency/75", "max", "sum", "75th percentile latency (ms)", "75th percentile latency (ms)", "n0");
            BenchmarksEventSource.Log.Metadata("signalr/latency/90", "max", "sum", "90th percentile latency (ms)", "90th percentile latency (ms)", "n0");
            BenchmarksEventSource.Log.Metadata("signalr/latency/99", "max", "sum", "99th percentile latency (ms)", "99th percentile latency (ms)", "n0");
            BenchmarksEventSource.Log.Metadata("signalr/latency/max", "max", "sum", "Max latency (ms)", "Max latency (ms)", "n0");
            if (CollectLatency)
            {
                var totalCount = 0;
                var totalSum = 0.0;
                for (var i = 0; i < _latencyPerConnection.Count; i++)
                {
                    for (var j = 0; j < _latencyPerConnection[i].Count; j++)
                    {
                        totalSum += _latencyPerConnection[i][j];
                        totalCount++;
                    }

                    _latencyPerConnection[i].Sort();
                }

                BenchmarksEventSource.Measure("signalr/latency/mean", totalSum / totalCount);

                var allConnections = new List<double>();
                foreach (var connectionLatency in _latencyPerConnection)
                {
                    allConnections.AddRange(connectionLatency);
                }

                // Review: Each connection can have different latencies, how do we want to deal with that?
                // We could just combine them all and ignore the fact that they are different connections
                // Or we could preserve the results for each one and record them separately
                allConnections.Sort();

                BenchmarksEventSource.Measure("signalr/latency/50", GetPercentile(50, allConnections));
                BenchmarksEventSource.Measure("signalr/latency/75", GetPercentile(75, allConnections));
                BenchmarksEventSource.Measure("signalr/latency/90", GetPercentile(90, allConnections));
                BenchmarksEventSource.Measure("signalr/latency/99", GetPercentile(99, allConnections));
                BenchmarksEventSource.Measure("signalr/latency/max", GetPercentile(100, allConnections));
            }
            else
            {
                var totalSum = 0.0;
                var totalCount = 0;
                foreach (var average in _latencyAverage)
                {
                    totalSum += average.sum;
                    totalCount += average.count;
                }

                if (totalCount != 0)
                {
                    totalSum /= totalCount;
                }

                BenchmarksEventSource.Measure("signalr/latency/mean", totalSum);
            }
        }

        private static double GetPercentile(int percent, List<double> sortedData)
        {
            if (percent == 100)
            {
                return sortedData[sortedData.Count - 1];
            }

            var i = ((long)percent * sortedData.Count) / 100.0 + 0.5;
            var fractionPart = i - Math.Truncate(i);

            return (1.0 - fractionPart) * sortedData[(int)Math.Truncate(i) - 1] + fractionPart * sortedData[(int)Math.Ceiling(i) - 1];
        }

        private static async Task CalculateClientToServerOffset()
        {
            var offsets = new List<TimeSpan>(9);
            for (var i = 0; i < 9; ++i)
            {
                var t0 = DateTime.UtcNow;
                var t1 = await _connections[0].InvokeAsync<DateTime>("GetCurrentTime");
                var t2 = DateTime.UtcNow;

                offsets.Add(((t1 - t0) + (t1 - t2)) / 2);
            }

            offsets.Sort();
            // Discard first 3 and last 3
            var range = offsets.GetRange(3, 3);
            var totalOffset = 0.0;
            foreach (var offset in range)
            {
                totalOffset += offset.TotalMilliseconds;
            }

            _clientToServerOffset = totalOffset / 3;
        }

        private static void Log(string message)
        {
            var time = DateTime.Now.ToString("hh:mm:ss.fff");
            Console.WriteLine($"[{time}] {message}");
        }
    }
}
