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
using Microsoft.AspNetCore.Sockets;
using Newtonsoft.Json;

namespace BenchmarksWorkers.Workers
{
    public class SignalRWorker : IWorker
    {
        public string JobLogText { get; set; }

        private ClientJob _job;
        private HttpClientHandler _httpClientHandler;
        private List<HubConnection> _connections;
        private List<IDisposable> _recvCallbacks;
        private Timer _timer;
        private List<int> _requestsPerConnection;
        private List<List<double>> _latencyPerConnection;
        private Stopwatch _workTimer = new Stopwatch();
        private bool _stopped;

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

            TransportType transportType = default;
            if (_job.ClientProperties.TryGetValue("TransportType", out var transport))
            {
                transportType = Enum.Parse<TransportType>(transport);
                jobLogText += $" TransportType:{transportType}";
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

            // SendAsync will return as soon as the request has been sent (non-blocking)
            await _connections[0].SendAsync("Echo", _job.Duration + 1);
            _workTimer.Start();
            _timer = new Timer(StopClients, null, TimeSpan.FromSeconds(_job.Duration), Timeout.InfiniteTimeSpan);
        }

        private async void StopClients(object t)
        {
            try
            {
                _timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
                await StopAsync();
            }
            finally
            {
                _job.State = ClientState.Completed;
            }
        }

        public async Task StopAsync()
        {
            if (_timer != null)
            {
                _timer?.Dispose();
                _timer = null;

                foreach (var callback in _recvCallbacks)
                {
                    // stops stat collection from happening quicker than StopAsync
                    // and we can do all the calculations while close is occurring
                    callback.Dispose();
                }

                _workTimer.Stop();

                _stopped = true;

                // stop connections
                Log("Stopping connections");
                var tasks = new List<Task>(_connections.Count);
                foreach (var connection in _connections)
                {
                    tasks.Add(connection.StopAsync());
                }

                CalculateStatistics();

                await Task.WhenAll(tasks);

                // TODO: Remove when clients no longer take a long time to "cool down"
                await Task.Delay(5000);

                Log("Stopped worker");
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

        private void CreateConnections(TransportType transportType = TransportType.WebSockets)
        {
            _connections = new List<HubConnection>(_job.Connections);
            _requestsPerConnection = new List<int>(_job.Connections);
            _latencyPerConnection = new List<List<double>>(_job.Connections);

            var hubConnectionBuilder = new HubConnectionBuilder()
                .WithUrl(_job.ServerBenchmarkUri)
                .WithMessageHandler(_httpClientHandler)
                //.WithConsoleLogger(Microsoft.Extensions.Logging.LogLevel.Trace) TODO: Support logging on client for debugging purposes
                .WithTransport(transportType);

            if (_job.ClientProperties.TryGetValue("HubProtocol", out var protocolName))
            {
                switch (protocolName)
                {
                    case "messagepack":
                        hubConnectionBuilder.WithMessagePackProtocol();
                        break;
                    case "json":
                        hubConnectionBuilder.WithJsonProtocol();
                        break;
                    default:
                        throw new Exception($"{protocolName} is an invalid hub protocol name.");
                }
            }
            else
            {
                hubConnectionBuilder.WithJsonProtocol();
            }

            foreach (var header in _job.Headers)
            {
                hubConnectionBuilder.WithHeader(header.Key, header.Value);
            }

            _recvCallbacks = new List<IDisposable>(_job.Connections);
            for (var i = 0; i < _job.Connections; i++)
            {
                _requestsPerConnection.Add(0);
                _latencyPerConnection.Add(new List<double>());

                var connection = hubConnectionBuilder.Build();
                _connections.Add(connection);

                // Capture the connection ID
                var id = i;
                // setup event handlers
                _recvCallbacks.Add(connection.On<DateTime>("echo", utcNow =>
                {
                    // TODO: Collect all the things
                    _requestsPerConnection[id] += 1;

                    var latency = DateTime.UtcNow - utcNow;
                    _latencyPerConnection[id].Add(latency.TotalMilliseconds);
                }));

                connection.Closed += e =>
                {
                    if (!_stopped)
                    {
                        Log($"Connection closed early: {e}");
                    }
                };
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
            Log($"Least Requests to Connection: {min}");
            Log($"Most Requests to Connection: {max}");

            var rps = (double)totalRequests / _workTimer.ElapsedMilliseconds * 1000;
            Log($"Total RPS: {rps}");
            _job.RequestsPerSecond = rps;

            // Latency
            Latency();
        }

        private void Latency()
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
                Log($"Average latency for connection #{i}: {avg[i]}");

                _latencyPerConnection[i].Sort();
                // Review: Each connection can have different latencies, how do we want to deal with that?
                // We could just combine them all and ignore the fact that they are different connections
                // Or we could preserve the results for each one and record them separately
                _job.Latency.Within50thPercentile = GetPercentile(50, _latencyPerConnection[i]);
                _job.Latency.Within75thPercentile = GetPercentile(75, _latencyPerConnection[i]);
                _job.Latency.Within90thPercentile = GetPercentile(90, _latencyPerConnection[i]);
                _job.Latency.Within99thPercentile = GetPercentile(99, _latencyPerConnection[i]);
                totalAvg += avg[i];
            }

            totalAvg /= avg.Count;
            _job.Latency.Average = totalAvg;
        }

        private double GetPercentile(int percent, List<double> sortedData)
        {
            var i = (percent * sortedData.Count) / 100.0 + 0.5;
            var fractionPart = i - Math.Truncate(i);

            return (1.0 - fractionPart) * sortedData[(int)Math.Truncate(i)] + fractionPart * sortedData[(int)Math.Ceiling(i)];
        }

        private static void Log(string message)
        {
            var time = DateTime.Now.ToString("hh:mm:ss.fff");
            Console.WriteLine($"[{time}] {message}");
        }
    }
}
