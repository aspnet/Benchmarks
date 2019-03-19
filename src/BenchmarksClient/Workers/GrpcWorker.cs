﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Benchmarks.ClientJob;
using Microsoft.AspNetCore.Http.Connections;
using Newtonsoft.Json;
using Grpc.Core;
using Greet;
using System.IO;
using Grpc.Core.Logging;

namespace BenchmarksClient.Workers
{
    public class GrpcWorker : IWorker
    {
        public string JobLogText { get; set; }

        private ClientJob _job;
        private List<Channel> _channels;
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
        private int _totalRequests;
        private bool _useTls;

        private void InitializeJob()
        {
            Log("Initialing gRPC worker");

            // MORE LOGGING
            //Environment.SetEnvironmentVariable("GRPC_TRACE", "api");
            //Environment.SetEnvironmentVariable("GRPC_VERBOSITY", "debug");
            //Grpc.Core.GrpcEnvironment.SetLogger(new CustomLogger(Log));

            _stopped = false;

            Debug.Assert(_job.Connections > 0, "There must be more than 0 connections");

            var jobLogText =
                $"[ID:{_job.Id} Connections:{_job.Connections} Duration:{_job.Duration} Method:{_job.Method} ServerUrl:{_job.ServerBenchmarkUri}";

            if (_job.ClientProperties.TryGetValue("CollectLatency", out var collectLatency))
            {
                if (bool.TryParse(collectLatency, out var toggle))
                {
                    _detailedLatency = toggle;
                }
            }

            if (_job.ClientProperties.TryGetValue("UseTls", out var useTls) && Convert.ToBoolean(useTls))
            {
                _useTls = true;
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
            if (_channels == null)
            {
                CreateChannels();
            }
        }

        #region Logger
        private class CustomLogger : ILogger
        {
            private readonly Action<string> _logger;

            public CustomLogger(Action<string> logger)
            {
                _logger = logger;
            }

            public void Debug(string message)
            {
                _logger("Debug - " + message);
            }

            public void Debug(string format, params object[] formatArgs)
            {
                _logger("Debug - " + string.Format(format, formatArgs));
            }

            public void Error(string message)
            {
                _logger("Error - " + message);
            }

            public void Error(string format, params object[] formatArgs)
            {
                _logger("Error - " + string.Format(format, formatArgs));
            }

            public void Error(Exception exception, string message)
            {
                _logger("Error - " + message + " " + exception?.ToString());
            }

            public ILogger ForType<T>()
            {
                return this;
            }

            public void Info(string message)
            {
                _logger("Info - " + message);
            }

            public void Info(string format, params object[] formatArgs)
            {
                _logger("Info - " + string.Format(format, formatArgs));
            }

            public void Warning(string message)
            {
                _logger("Warning - " + message);
            }

            public void Warning(string format, params object[] formatArgs)
            {
                _logger("Warning - " + string.Format(format, formatArgs));
            }

            public void Warning(Exception exception, string message)
            {
                _logger("Warning - " + message + " " + exception?.ToString());
            }
        }
        #endregion

        public async Task StartJobAsync(ClientJob job)
        {
            _job = job;
            Log($"Starting Job");
            InitializeJob();

            _job.State = ClientState.Running;
            _job.LastDriverCommunicationUtc = DateTime.UtcNow;

            var cts = new CancellationTokenSource();
            cts.CancelAfter(TimeSpan.FromSeconds(_job.Duration));
            cts.Token.Register(() =>
            {
                Log("Benchmark duration complete");
            });
            _workTimer.Restart();

            var callTasks = new List<Task>();

            try
            {
                switch (_scenario)
                {
                    case "Unary":
                        Log($"Starting {_scenario}");

                        for (var i = 0; i < _channels.Count; i++)
                        {
                            var id = i;
                            // kick off a task per channel so they don't wait for other channels when sending "SayHello"
                            var task = Task.Run(async () =>
                            {
                                Log($"{id}: Starting {_scenario}");

                                var client = new Greeter.GreeterClient(_channels[id]);

                                while (!cts.IsCancellationRequested)
                                {
                                    try
                                    {
                                        var start = DateTime.UtcNow;
                                        var response = await client.SayHelloAsync(new HelloRequest
                                        {
                                            Name = "World"
                                        });
                                        var end = DateTime.UtcNow;

                                        ReceivedDateTime(start, end, id);
                                    }
                                    catch (Exception ex)
                                    {
                                        Log($"{id}: Error message: {ex.Message}");
                                    }
                                }

                                Log($"{id}: Finished {_scenario}");
                            });

                            callTasks.Add(task);
                        }

                        Log($"Finished {_scenario}");
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

            Log($"Waiting on duration");
            cts.Token.WaitHandle.WaitOne();

            Log($"Waiting for call tasks to complete");
            await Task.WhenAll(callTasks);

            Log($"Stopping job");
            await StopJobAsync();
        }

        public async Task StopJobAsync()
        {
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

            // stop channels
            Log("Stopping channels");
            var tasks = new List<Task>(_channels.Count);
            foreach (var channel in _channels)
            {
                tasks.Add(channel.ShutdownAsync());
            }

            await Task.WhenAll(tasks);
            Log("Channels have been disposed");

            // TODO: Remove when clients no longer take a long time to "cool down"
            await Task.Delay(5000);

            Log("Stopped worker");
        }

        public void Dispose()
        {
            DisposeAsync().GetAwaiter().GetResult();
        }

        private void CreateChannels()
        {
            _channels = new List<Channel>(_job.Connections);
            _requestsPerConnection = new List<int>(_job.Connections);
            _latencyPerConnection = new List<List<double>>(_job.Connections);
            _latencyAverage = new List<(double sum, int count)>(_job.Connections);

            _recvCallbacks = new List<IDisposable>(_job.Connections);

            // Channel does not care about scheme
            var initialUri = new Uri(_job.ServerBenchmarkUri);
            var resolvedUri = initialUri.Authority;

            Log($"Creating channels to '{resolvedUri}'");
            Log($"Channels authenticated with TLS: {_useTls}");

            for (var i = 0; i < _job.Connections; i++)
            {
                _requestsPerConnection.Add(0);
                _latencyPerConnection.Add(new List<double>());
                _latencyAverage.Add((0, 0));

                var channelCredentials = _useTls ? GetSslCredentials() : ChannelCredentials.Insecure;

                var channel = new Channel(resolvedUri, channelCredentials);
                _channels.Add(channel);

                channel.ShutdownToken.Register(() =>
                {
                    if (!_stopped)
                    {
                        var error = $"Channel closed early";
                        _job.Error += Environment.NewLine + $"[{DateTime.Now.ToString("hh:mm:ss.fff")}] " + error;
                        Log(error);
                    }
                });
            }
        }

        private void ReceivedDateTime(DateTime start, DateTime end, int channelId)
        {
            if (_stopped)
            {
                return;
            }

            _requestsPerConnection[channelId] += 1;

            var latency = end - start;
            latency = latency.Add(TimeSpan.FromMilliseconds(_clientToServerOffset));
            if (_detailedLatency)
            {
                _latencyPerConnection[channelId].Add(latency.TotalMilliseconds);
            }
            else
            {
                var (sum, count) = _latencyAverage[channelId];
                sum += latency.TotalMilliseconds;
                count++;
                _latencyAverage[channelId] = (sum, count);
            }
        }

        private void CalculateStatistics()
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
                for (var i = 0; i < _latencyPerConnection.Count; i++)
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

        private static SslCredentials _credentials;

        private static SslCredentials GetSslCredentials()
        {
            if (_credentials == null)
            {
                Log($"Loading credentials from '{AppContext.BaseDirectory}'");

                _credentials = new SslCredentials(
                    File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Certs", "ca.crt")),
                    new KeyCertificatePair(
                        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Certs", "client.crt")),
                        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Certs", "client.key"))));
            }

            return _credentials;
        }
    }
}
