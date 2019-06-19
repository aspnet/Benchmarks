// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Benchmarks.ClientJob;
using Ignitor;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.SignalR.Protocol;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace BenchmarksClient.Workers
{
    public partial class BlazorIgnitor : IWorker
    {
        private ClientJob _job;
        private HttpClientHandler _httpClientHandler;
        private List<BlazorServerItem> _connections;
        private List<IDisposable> _recvCallbacks;
        private int[] _requestsPerConnection;
        private List<double>[] _latencyPerConnection;
        private Stopwatch _workTimer = new Stopwatch();
        private bool _stopped;
        private SemaphoreSlim _lock = new SemaphoreSlim(1);
        private bool _detailedLatency;
        private string _scenario;
        private (double sum, int count)[] _latencyAverage;
        private int _totalRequests;
        private HttpClient _httpClient;
        private CancellationTokenSource _cancelationTokenSource;

        private int OperationDelay;

        public string JobLogText { get; set; }

        private Task InitializeJob()
        {
            _stopped = false;

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

            if (_job.ClientProperties.TryGetValue("Scenario", out var scenario))
            {
                _scenario = scenario;
                jobLogText += $" Scenario:{scenario}";
            }
            else
            {
                throw new Exception("Scenario wasn't specified");
            }

            var operationDelay = 0; // Time in ms
            if (_job.ClientProperties.TryGetValue("OperationDelay", out var operationDelayString))
            {
                operationDelay = int.Parse(operationDelayString);
            }

            OperationDelay = operationDelay;

            jobLogText += "]";
            JobLogText = jobLogText;
            if (_connections == null)
            {
                return CreateConnections();
            }

            return Task.CompletedTask;
        }

        public async Task StartJobAsync(ClientJob job)
        {
            _job = job;
            Log($"Starting Job");
            await InitializeJob();
            // start connections
            var tasks = new List<Task>(_connections.Count);
            foreach (var item in _connections)
            {
                tasks.Add(item.HubConnection.StartAsync());
            }

            await Task.WhenAll(tasks);

            _job.State = ClientState.Running;
            _job.LastDriverCommunicationUtc = DateTime.UtcNow;

            _cancelationTokenSource = new CancellationTokenSource();
            _cancelationTokenSource.CancelAfter(TimeSpan.FromSeconds(_job.Duration));
            _workTimer.Restart();

            try
            {
                switch (_scenario)
                {
                    case "Navigator":
                        await Navigator(_cancelationTokenSource.Token);
                        break;

                    case "Clicker":
                        await Clicker(_cancelationTokenSource.Token);
                        break;

                    case "Reconnects":
                        await Reconnects(_cancelationTokenSource.Token);
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

            _cancelationTokenSource.Token.WaitHandle.WaitOne();
            await StopJobAsync();
        }

        public async Task StopJobAsync()
        {
            _cancelationTokenSource.Cancel();

            Log($"Stopping Job: {_job.SpanId}");
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
                _job.State = ClientState.Completed;
                _job.ActualDuration = _workTimer.Elapsed;
            }
        }

        // We want to move code from StopAsync into Release(). Any code that would prevent
        // us from reusing the connnections.
        public async Task DisposeAsync()
        {
            foreach (var callback in _recvCallbacks)
            {
                // stops stat collection from happening quicker than StopAsync
                // and we can do all the calculations while close is occurring
                callback.Dispose();
            }

            // stop connections
            Log("Stopping connections");
            var tasks = new List<Task>(_connections.Count);
            foreach (var item in _connections)
            {
                tasks.Add(item.HubConnection.DisposeAsync());
            }

            await Task.WhenAll(tasks);
            Log("Connections have been disposed");

            _httpClientHandler.Dispose();
            // TODO: Remove when clients no longer take a long time to "cool down"
            await Task.Delay(5000);

            Log("Stopped worker");
        }

        public void Dispose()
        {
            var tasks = new List<Task>(_connections.Count);
            foreach (var item in _connections)
            {
                tasks.Add(item.HubConnection.DisposeAsync());
            }

            Task.WhenAll(tasks).GetAwaiter().GetResult();

            _httpClientHandler.Dispose();
        }

        private async Task CreateConnections()
        {
            _connections = new List<BlazorServerItem>(_job.Connections);
            _requestsPerConnection = new int[_job.Connections];
            _latencyPerConnection = new List<double>[_job.Connections];
            _latencyAverage = new (double sum, int count)[_job.Connections];

            var baseUri = new Uri(_job.ServerBenchmarkUri);

            _httpClient = new HttpClient { BaseAddress = baseUri, Timeout = TimeSpan.FromSeconds(5), };

            _recvCallbacks = new List<IDisposable>(_job.Connections);
            for (var i = 0; i < _job.Connections; i++)
            {
                var response = await _httpClient.GetAsync("");
                var content = await response.Content.ReadAsStringAsync();

                // <!-- M.A.C.Component:{"circuitId":"CfDJ8KZCIaqnXmdF...PVd6VVzfnmc1","rendererId":"0","componentId":"0"} -->
                var match = Regex.Match(content, $"{Regex.Escape("<!-- M.A.C.Component:")}(.+?){Regex.Escape(" -->")}");
                using var json = JsonDocument.Parse(match.Groups[1].Value);
                var circuitId = json.RootElement.GetProperty("circuitId").GetString();

                var builder = new HubConnectionBuilder();
                builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IHubProtocol, IgnitorMessagePackHubProtocol>());
                builder.WithUrl(new Uri(baseUri, "_blazor/"));

                if (_job.ClientProperties.TryGetValue("LogLevel", out var logLevel))
                {
                    if (Enum.TryParse<LogLevel>(logLevel, ignoreCase: true, result: out var level))
                    {
                        builder.ConfigureLogging(builder =>
                        {
                            builder.SetMinimumLevel(level);
                        });
                    }
                }

                var connection = builder.Build();
                _connections.Add(new BlazorServerItem
                {
                    HubConnection = connection,
                    CircuitId = circuitId,
                    BaseUri = baseUri.ToString()

                });
            }
        }

        private void CalculateStatistics()
        {
            // RPS
            var newTotalRequests = 0;
            var min = int.MaxValue;
            var max = 0;
            for (var i = 0; i < _requestsPerConnection.Length; i++)
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

            var requestDelta = newTotalRequests - _totalRequests;
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
            _job.RequestsPerSecond = rps;
            _job.Requests = requestDelta;

            // Latency
            CalculateLatency();
        }

        private void CalculateLatency()
        {
            if (_detailedLatency)
            {
                var totalCount = 0;
                var totalSum = 0.0;
                for (var i = 0; i < _latencyPerConnection.Length; i++)
                {
                    for (var j = 0; j < _latencyPerConnection[i].Count; j++)
                    {
                        totalSum += _latencyPerConnection[i][j];
                        totalCount++;
                    }

                    _latencyPerConnection[i].Sort();
                }

                _job.Latency.Average = totalSum / totalCount;

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
                _job.Latency.MaxLatency = GetPercentile(100, allConnections);
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
                _job.Latency.Average = totalSum;
            }
        }

        private double GetPercentile(int percent, List<double> sortedData)
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

        private class BlazorServerItem
        {
            public HubConnection HubConnection { get; set; }

            public string CircuitId { get; set; }

            public string BaseUri { get; set; }
        }
    }
}
