// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using Benchmarks.ClientJob;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Microsoft.Extensions.DependencyInjection;

namespace BenchmarksClient.Workers
{
    public class SignalRWorker : IWorker
    {
        public string JobLogText { get; set; }

        private ClientJob _job;
        private HttpClientHandler _httpClientHandler;
        private List<HubConnection> _connections;
        private List<IDisposable> _recvCallbacks;
        private List<int> _requestsPerConnection;
        private List<List<double>> _latencyPerConnection;
        private Stopwatch _workTimer = new Stopwatch();
        private bool _stopped;
        private SemaphoreSlim _lock = new SemaphoreSlim(1);
        private bool _detailedLatency;
        private string _scenario;
        private List<(double sum, int count)> _latencyAverage;
        private double _clientToServerOffset;

        public SignalRWorker(ClientJob job)
        {
            _job = job;

            Debug.Assert(_job.Connections > 0, "There must be more than 0 connections");

            // Configuring the http client to trust the self-signed certificate
            _httpClientHandler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
            };

            var jobLogText =
                $"[ID:{_job.Id} Connections:{_job.Connections} Duration:{_job.Duration} Method:{_job.Method} ServerUrl:{_job.ServerBenchmarkUri}";

            if (_job.Headers != null)
            {
                jobLogText += $" Headers:{JsonConvert.SerializeObject(_job.Headers)}";
            }

            HttpTransportType transportType = default;
            if (_job.ClientProperties.TryGetValue("TransportType", out var transport))
            {
                transportType = Enum.Parse<HttpTransportType>(transport);
                jobLogText += $" TransportType:{transportType}";
            }

            if (_job.ClientProperties.TryGetValue("HubProtocol", out var protocol))
            {
                jobLogText += $" HubProtocol:{protocol}";
            }

            if (_job.ClientProperties.TryGetValue("CollectLatency", out var collectLatency))
            {
                if (bool.TryParse(collectLatency, out var toggle))
                {
                    _detailedLatency = toggle;
                }
            }

            if (_job.ClientProperties.TryGetValue("Scenario", out var scenario))
            {
                _scenario = scenario;
                jobLogText += $" Scenario:{scenario}";
            }
            else
            {
                throw new Exception("Scenario wasn't specified");
            }

            jobLogText += "]";
            JobLogText = jobLogText;

            CreateConnections(transportType);
        }

        public async Task StartAsync()
        {
            // start connections
            var tasks = new List<Task>(_connections.Count);
            foreach (var connection in _connections)
            {
                tasks.Add(connection.StartAsync());
            }

            await Task.WhenAll(tasks);

            _job.State = ClientState.Running;
            _job.LastDriverCommunicationUtc = DateTime.UtcNow;

            var cts = new CancellationTokenSource();
            cts.CancelAfter(TimeSpan.FromSeconds(_job.Duration));

            _workTimer.Start();

            try
            {
                switch (_scenario)
                {
                    case "broadcast":
                        await CalculateClientToServerOffset();
                        // SendAsync will return as soon as the request has been sent (non-blocking)
                        await _connections[0].SendAsync("Broadcast", _job.Duration + 1);
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
                    default:
                        throw new Exception($"Scenario '{_scenario}' is not a known scenario.");
                }
            }
            catch (Exception ex)
            {
                var text = "Exception from test: " + ex.Message;
                Log(text);
                _job.Error += Environment.NewLine + text;
            }

            cts.Token.WaitHandle.WaitOne();
            await StopAsync();
        }

        public async Task StopAsync()
        {
            if (_stopped || !await _lock.WaitAsync(0))
            {
                // someone else is stopping, we only need to do it once
                return;
            }
            try
            {
                _stopped = true;
                _workTimer.Stop();

                foreach (var callback in _recvCallbacks)
                {
                    // stops stat collection from happening quicker than StopAsync
                    // and we can do all the calculations while close is occurring
                    callback.Dispose();
                }

                // stop connections
                Log("Stopping connections");
                var tasks = new List<Task>(_connections.Count);
                foreach (var connection in _connections)
                {
                    tasks.Add(connection.DisposeAsync());
                }

                CalculateStatistics();

                await Task.WhenAll(tasks);

                // TODO: Remove when clients no longer take a long time to "cool down"
                await Task.Delay(5000);

                Log("Stopped worker");
            }
            finally
            {
                _lock.Release();
                _job.State = ClientState.Completed;
            }
        }

        public void Dispose()
        {
            var tasks = new List<Task>(_connections.Count);
            foreach (var connection in _connections)
            {
                tasks.Add(connection.DisposeAsync());
            }

            Task.WhenAll(tasks).GetAwaiter().GetResult();

            _httpClientHandler.Dispose();
        }

        private void CreateConnections(HttpTransportType transportType = HttpTransportType.WebSockets)
        {
            _connections = new List<HubConnection>(_job.Connections);
            _requestsPerConnection = new List<int>(_job.Connections);
            _latencyPerConnection = new List<List<double>>(_job.Connections);
            _latencyAverage = new List<(double sum, int count)>(_job.Connections);

            _recvCallbacks = new List<IDisposable>(_job.Connections);
            for (var i = 0; i < _job.Connections; i++)
            {
                var hubConnectionBuilder = new HubConnectionBuilder()
                .WithUrl(_job.ServerBenchmarkUri, httpConnectionOptions =>
                {
                    httpConnectionOptions.HttpMessageHandlerFactory = _ => _httpClientHandler;
                    httpConnectionOptions.Transports = transportType;

                    // REVIEW: Is there a CopyTo overload or something that turns this into a one liner?
                    foreach (var pair in _job.Headers)
                    {
                        httpConnectionOptions.Headers.Add(pair.Key, pair.Value);
                    }
                });

                if (_job.ClientProperties.TryGetValue("LogLevel", out var logLevel))
                {
                    if (Enum.TryParse<LogLevel>(logLevel, ignoreCase: true, result: out var level))
                    {
                        hubConnectionBuilder.ConfigureLogging(builder =>
                        {
                            builder.AddConsole();
                            builder.SetMinimumLevel(level);
                        });
                    }
                }

                if (_job.ClientProperties.TryGetValue("HubProtocol", out var protocolName))
                {
                    switch (protocolName)
                    {
                        case "messagepack":
                            hubConnectionBuilder.AddMessagePackProtocol();
                            break;
                        case "json":
                            // json hub protocol is set by default
                            break;
                        default:
                            throw new Exception($"{protocolName} is an invalid hub protocol name.");
                    }
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
                        _job.Error += Environment.NewLine + $"[{DateTime.Now.ToString("hh:mm:ss.fff")}] " + error;
                        Log(error);
                    }

                    return Task.CompletedTask;
                };
            }
        }

        private void ReceivedDateTime(DateTime dateTime, int connectionId)
        {
            if (_stopped)
            {
                return;
            }

            _requestsPerConnection[connectionId] += 1;

            var latency = DateTime.UtcNow - dateTime;
            latency = latency.Add(TimeSpan.FromMilliseconds(_clientToServerOffset));
            if (_detailedLatency)
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

        private void CalculateStatistics()
        {
            // RPS
            var totalRequests = 0;
            var min = int.MaxValue;
            var max = 0;
            for (var i = 0; i < _requestsPerConnection.Count; i++)
            {
                totalRequests += _requestsPerConnection[i];

                if (_requestsPerConnection[i] > max)
                {
                    max = _requestsPerConnection[i];
                }
                if (_requestsPerConnection[i] < min)
                {
                    min = _requestsPerConnection[i];
                }
            }
            // Review: This could be interesting information, see the gap between most active and least active connection
            // Ideally they should be within a couple percent of each other, but if they aren't something could be wrong
            Log($"Least Requests per Connection: {min}");
            Log($"Most Requests per Connection: {max}");

            if (_workTimer.ElapsedMilliseconds <= 0)
            {
                Log("Job failed to run");
                return;
            }

            var rps = (double)totalRequests / _workTimer.ElapsedMilliseconds * 1000;
            Log($"Total RPS: {rps}");
            _job.RequestsPerSecond = rps;
            _job.Requests = totalRequests;

            // Latency
            CalculateLatency();
        }

        private void CalculateLatency()
        {
            if (_detailedLatency)
            {
                var avg = new List<double>(_latencyPerConnection.Count);
                var totalAvg = 0.0;
                for (var i = 0; i < _latencyPerConnection.Count; i++)
                {
                    avg.Add(0.0);
                    for (var j = 0; j < _latencyPerConnection[i].Count; j++)
                    {
                        avg[i] += _latencyPerConnection[i][j];
                    }
                    avg[i] /= _latencyPerConnection[i].Count;

                    _latencyPerConnection[i].Sort();
                    totalAvg += avg[i];
                }

                totalAvg /= avg.Count;
                _job.Latency.Average = totalAvg;

                var allConnections = new List<double>();
                foreach (var connectionLatency in _latencyPerConnection)
                {
                    allConnections.AddRange(connectionLatency);
                }

                // Review: Each connection can have different latencies, how do we want to deal with that?
                // We could just combine them all and ignore the fact that they are different connections
                // Or we could preserve the results for each one and record them separately
                allConnections.Sort();
                _job.Latency.Within50thPercentile = GetPercentile(50, allConnections);
                _job.Latency.Within75thPercentile = GetPercentile(75, allConnections);
                _job.Latency.Within90thPercentile = GetPercentile(90, allConnections);
                _job.Latency.Within99thPercentile = GetPercentile(99, allConnections);
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

                totalSum /= totalCount;
                _job.Latency.Average = totalSum;
            }
        }

        private double GetPercentile(int percent, List<double> sortedData)
        {
            var i = (percent * sortedData.Count) / 100.0 + 0.5;
            var fractionPart = i - Math.Truncate(i);

            return (1.0 - fractionPart) * sortedData[(int)Math.Truncate(i)] + fractionPart * sortedData[(int)Math.Ceiling(i)];
        }

        private async Task CalculateClientToServerOffset()
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
