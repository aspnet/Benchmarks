// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Diagnostics;
using System.Reflection;
using McMaster.Extensions.CommandLineUtils;
using System.Net.WebSockets;
using Microsoft.Crank.EventSources;

namespace WebsocketClient
{
    class Program
    {
        private const int EchoMessageSize = 1000;

        private static List<ClientWebSocket> _connections;
        private static List<int> _requestsPerConnection;
        private static List<int> _errorsPerConnection;
        private static List<List<double>> _latencyPerConnection;
        private static List<(double sum, int count)> _latencyAverage;
        private static Stopwatch _workTimer = new Stopwatch();
        private static List<Stopwatch> _echoTimers;
        private static bool _stopped;
        private static SemaphoreSlim _lock = new SemaphoreSlim(1);
        private static int _totalRequests;
        private static int _totalErrors;

        public static string ServerUrl { get; set; }
        public static string Scenario { get; set; }
        public static int WarmupTimeSeconds { get; set; }
        public static int ExecutionTimeSeconds { get; set; }
        public static int Connections { get; set; }
        public static bool CollectLatency { get; set; }

        static async Task Main(string[] args)
        {
            var app = new CommandLineApplication();

            app.HelpOption("-h|--help");
            var optionUrl = app.Option("-u|--url <URL>", "The server url to request", CommandOptionType.SingleValue);
            var optionConnections = app.Option<int>("-c|--connections <N>", "Total number of Websocket connections to keep open", CommandOptionType.SingleValue);
            var optionWarmup = app.Option<int>("-w|--warmup <N>", "Duration of the warmup in seconds", CommandOptionType.SingleValue);
            var optionDuration = app.Option<int>("-d|--duration <N>", "Duration of the test in seconds", CommandOptionType.SingleValue);
            var optionScenario = app.Option("-s|--scenario <S>", "Scenario to run", CommandOptionType.SingleValue);
            var optionLatency = app.Option<bool>("-l|--latency <B>", "Whether to collect detailed latency", CommandOptionType.SingleOrNoValue);

            app.OnExecuteAsync(cancellationToken =>
            {
                Log("Websocket Client starting");

                BenchmarksEventSource.Log.Metadata("websocket/client-version", "first", "first", "Client Version", "Client Version", "object");
                BenchmarksEventSource.Measure("websocket/client-version", typeof(ClientWebSocket).GetTypeInfo().Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.ToString());

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

                return RunAsync();
            });

            await app.ExecuteAsync(args);
        }

        private static async Task RunAsync()
        {
            await CreateConnections();

            if (WarmupTimeSeconds > 0)
            {
                Log($"Warming up for {WarmupTimeSeconds}s...");
                await StartScenario(WarmupTimeSeconds);
            }

            ResetCounters();
            Log($"Running for {ExecutionTimeSeconds}s...");
            await StartScenario(ExecutionTimeSeconds);

            await CloseConnections();

            await StopJobAsync();
        }

        private static async Task StartScenario(int executionTimeSeconds)
        {
            Log($"Starting scenario {Scenario}");

            try
            {
                var tasks = new List<Task>(_connections.Count);

                var cts = new CancellationTokenSource();
                // var tcs = new TaskCompletionSource<object>();
                // cts.Token.Register(() => tcs.SetResult(null));
                cts.CancelAfter(TimeSpan.FromSeconds(executionTimeSeconds));
                _workTimer.Restart();

                switch (Scenario)
                {
                    case "echo":
                        var random = new Random();
                        for (var i = 0; i < _connections.Count; i++)
                        {
                            var id = i;

                            var message = new byte[EchoMessageSize];
                            random.NextBytes(message);
                            // kick off a task per connection so they don't wait for other connections when sending "Echo"
                            tasks.Add(Task.Run(async () =>
                            {
                                var buffer = new byte[1024 * 4];
                                while (!cts.IsCancellationRequested)
                                {
                                    var stopped = await Echo(id, message, buffer);
                                    if (stopped)
                                    {
                                        break;
                                    }
                                }
                            }));
                        }
                        break;
                    default:
                        throw new Exception($"Scenario '{Scenario}' is not a known scenario.");
                }

                await Task.WhenAll(tasks);
            }
            catch (Exception ex)
            {
                Log("Exception from test: " + ex.Message);
            }
        }

        private static async Task<bool> Echo(int id, byte[] message, byte[] buffer)
        {
            if (_connections[id].State != WebSocketState.Open)
            {
                Log($"Connection {id} closed.");
                return true;
            }

            _echoTimers[id].Restart();
            try
            {
                await _connections[id].SendAsync(message.AsMemory(), WebSocketMessageType.Text, true, CancellationToken.None);

                var response = await _connections[id].ReceiveAsync(buffer.AsMemory(), CancellationToken.None);
                while (true)
                {
                    if (response.MessageType == WebSocketMessageType.Close)
                    {
                        if (!_stopped)
                        {
                            Log($"Connection {id} closed early.");
                            _errorsPerConnection[id] += 1;
                        }
                        return true;
                    }

                    if (response.EndOfMessage)
                    {
                        break;
                    }
                    else
                    {
                        response = await _connections[id].ReceiveAsync(buffer.AsMemory(), CancellationToken.None);
                    }
                }
            }
            catch
            {
                _errorsPerConnection[id] += 1;
            }

            _echoTimers[id].Stop();
            LogLatency(_echoTimers[id].Elapsed, id);

            return false;
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
        }

        private static async Task CreateConnections()
        {
            _connections = new List<ClientWebSocket>(Connections);
            _requestsPerConnection = new List<int>(Connections);
            _errorsPerConnection = new List<int>(Connections);
            _latencyPerConnection = new List<List<double>>(Connections);
            _latencyAverage = new List<(double sum, int count)>(Connections);
            _echoTimers = new List<Stopwatch>(Connections);

            for (var i = 0; i < Connections; i++)
            {
                var client = new ClientWebSocket();
                await client.ConnectAsync(new Uri(ServerUrl), CancellationToken.None);

                _requestsPerConnection.Add(0);
                _errorsPerConnection.Add(0);
                _latencyPerConnection.Add(new List<double>());
                _latencyAverage.Add((0, 0));

                _connections.Add(client);
                _echoTimers.Add(new Stopwatch());
            }
        }

        private static async Task CloseConnections()
        {
            for (var i = 0; i < Connections; i++)
            {
                await _connections[i].CloseAsync(WebSocketCloseStatus.NormalClosure, "Benchmark complete", CancellationToken.None);
            }
        }

        private static void ResetCounters()
        {
            for (var i = 0; i < Connections; i++)
            {
                _requestsPerConnection[i] = 0;
                _errorsPerConnection[i] = 0;
                _latencyPerConnection[i] = new List<double>();
                _latencyAverage[i] = (0, 0);
            }
        }

        private static void LogLatency(TimeSpan latency, int connectionId)
        {
            if (_stopped)
            {
                return;
            }

            _requestsPerConnection[connectionId] += 1;

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
            int errorDelta = 0;
            var newTotalRequests = 0;
            var newTotalErrors = 0;
            var minRequests = int.MaxValue;
            var maxRequests = 0;
            var minErrors = int.MaxValue;
            var maxErrors = 0;
            for (var i = 0; i < Connections; i++)
            {
                newTotalRequests += _requestsPerConnection[i];
                newTotalErrors += _errorsPerConnection[i];

                if (_requestsPerConnection[i] > maxRequests)
                {
                    maxRequests = _requestsPerConnection[i];
                }
                if (_requestsPerConnection[i] < minRequests)
                {
                    minRequests = _requestsPerConnection[i];
                }

                if (_errorsPerConnection[i] > maxErrors)
                {
                    maxErrors = _errorsPerConnection[i];
                }
                if (_errorsPerConnection[i] < minErrors)
                {
                    minErrors = _errorsPerConnection[i];
                }
            }

            requestDelta = newTotalRequests - _totalRequests;
            _totalRequests = newTotalRequests;

            errorDelta = newTotalErrors - _totalErrors;
            _totalErrors = newTotalErrors;

            // Review: This could be interesting information, see the gap between most active and least active connection
            // Ideally they should be within a couple percent of each other, but if they aren't something could be wrong
            Log($"Least Requests per Connection: {minRequests}");
            Log($"Most Requests per Connection: {maxRequests}");
            Log($"Least Errors per Connection: {minErrors}");
            Log($"Most Errors per Connection: {maxErrors}");

            if (_workTimer.ElapsedMilliseconds <= 0)
            {
                Log("Job failed to run");
                return;
            }

            var rps = (double)requestDelta / _workTimer.ElapsedMilliseconds * 1000;
            Log($"Total RPS: {rps}");
            Log($"Total Errors: {_totalErrors}");

            BenchmarksEventSource.Log.Metadata("websocket/rps/max", "max", "sum", "Max RPS", "RPS: max", "n0");
            BenchmarksEventSource.Log.Metadata("websocket/requests", "max", "sum", "Requests", "Total number of requests", "n0");
            BenchmarksEventSource.Log.Metadata("websocket/errors", "max", "sum", "Errors", "Total number of errors", "n0");

            BenchmarksEventSource.Measure("websocket/rps/max", rps);
            BenchmarksEventSource.Measure("websocket/requests", requestDelta);
            BenchmarksEventSource.Measure("websocket/errors", errorDelta);

            // Latency
            CalculateLatency();
        }

        private static void CalculateLatency()
        {
            BenchmarksEventSource.Log.Metadata("websocket/latency/mean", "max", "sum", "Mean latency (ms)", "Mean latency (ms)", "n0");
            BenchmarksEventSource.Log.Metadata("websocket/latency/50", "max", "sum", "50th percentile latency (ms)", "50th percentile latency (ms)", "n0");
            BenchmarksEventSource.Log.Metadata("websocket/latency/75", "max", "sum", "75th percentile latency (ms)", "75th percentile latency (ms)", "n0");
            BenchmarksEventSource.Log.Metadata("websocket/latency/90", "max", "sum", "90th percentile latency (ms)", "90th percentile latency (ms)", "n0");
            BenchmarksEventSource.Log.Metadata("websocket/latency/99", "max", "sum", "99th percentile latency (ms)", "99th percentile latency (ms)", "n0");
            BenchmarksEventSource.Log.Metadata("websocket/latency/max", "max", "sum", "Max latency (ms)", "Max latency (ms)", "n0");
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

                BenchmarksEventSource.Measure("websocket/latency/mean", totalSum / totalCount);

                var allConnections = new List<double>();
                foreach (var connectionLatency in _latencyPerConnection)
                {
                    allConnections.AddRange(connectionLatency);
                }

                allConnections.Sort();

                BenchmarksEventSource.Measure("websocket/latency/50", GetPercentile(50, allConnections));
                BenchmarksEventSource.Measure("websocket/latency/75", GetPercentile(75, allConnections));
                BenchmarksEventSource.Measure("websocket/latency/90", GetPercentile(90, allConnections));
                BenchmarksEventSource.Measure("websocket/latency/99", GetPercentile(99, allConnections));
                BenchmarksEventSource.Measure("websocket/latency/max", GetPercentile(100, allConnections));
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

                BenchmarksEventSource.Measure("websocket/latency/mean", totalSum);
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

        private static void Log(string message)
        {
            var time = DateTime.Now.ToString("hh:mm:ss.fff");
            Console.WriteLine($"[{time}] {message}");
        }
    }
}
