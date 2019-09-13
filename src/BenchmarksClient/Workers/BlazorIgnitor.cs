// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Benchmarks.ClientJob;
using Ignitor;
using Newtonsoft.Json;

namespace BenchmarksClient.Workers
{
    public partial class BlazorIgnitor : IWorker
    {
        private ClientJob _job;
        private List<BlazorClient> _clients;
        private List<ClientStatistics> _clientStatistics;
        private Stopwatch _workTimer = new Stopwatch();
        private bool _stopped;
        private SemaphoreSlim _lock = new SemaphoreSlim(1);
        private bool _detailedLatency;
        private string _scenario;
        private int _totalRequests;
        private CancellationTokenSource _cancelationTokenSource;

        public string JobLogText { get; set; }

        private Task InitializeJob()
        {
            _stopped = false;

            _detailedLatency = _job.Latency == null;

            Debug.Assert(_job.Connections > 0, "There must be more than 0 connections");

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

            jobLogText += "]";
            JobLogText = jobLogText;

            _clients = Enumerable.Range(0, _job.Connections).Select(_ => new BlazorClient()).ToList();
            _clientStatistics = Enumerable.Range(0, _job.Connections).Select(_ => new ClientStatistics()).ToList();

            return Task.CompletedTask;
        }

        public async Task StartJobAsync(ClientJob job)
        {
            _job = job;
            Log($"Starting Job");
            await InitializeJob();
            // start connections

            var tasks = new List<Task>(_clients.Count);
            foreach (var client in _clients)
            {
                tasks.Add(client.ConnectAsync(new Uri(_job.ServerBenchmarkUri)));
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
                    case "Basic":
                        await Basic(_cancelationTokenSource.Token);
                        break;

                    case "FormInput":
                        await FormInput(_cancelationTokenSource.Token);
                        break;

                    case "BackgroundUpdates":
                        await BackgroundUpdates(_cancelationTokenSource.Token);
                        break;

                    default:
                        throw new Exception($"Scenario '{_scenario}' is not a known scenario.");
                }
            }
            catch (Exception ex)
            {
                var text = "Exception from test: " + ex;
                Log(text);
                _job.Error += Environment.NewLine + text;
            }

            _cancelationTokenSource.Token.WaitHandle.WaitOne();
            await StopJobAsync();
        }

        public async Task StopJobAsync()
        {
            _cancelationTokenSource?.Cancel();

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
            // stop connections
            Log("Stopping connections");
            var tasks = new List<Task>(_clients.Count);
            foreach (var item in _clients)
            {
                tasks.Add(item.DisposeAsync().AsTask());
            }

            await Task.WhenAll(tasks);
            Log("Connections have been disposed");

            // TODO: Remove when clients no longer take a long time to "cool down"
            await Task.Delay(5000);

            Log("Stopped worker");
        }

        public void Dispose()
        {
            DisposeAsync().GetAwaiter().GetResult();
        }

        private void CalculateStatistics()
        {
            // RPS
            var newTotalRequests = 0;
            var min = 0;
            var max = 0;
            foreach (var client in _clientStatistics)
            {
                newTotalRequests += client.Renders;
                max = Math.Max(max, client.Renders);
                min = Math.Max(min, client.Renders);
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
                var allConnections = new List<double>();
                foreach (var client in _clientStatistics)
                {
                    totalCount += client.LatencyPerRender.Count;
                    totalSum += client.LatencyPerRender.Sum();

                    allConnections.AddRange(client.LatencyPerRender);
                }

                _job.Latency.Average = totalSum / totalCount;

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
                foreach (var client in _clientStatistics)
                {
                    totalSum += client.LatencyAverage.sum;
                    totalCount += client.LatencyAverage.count;
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

        private class ClientStatistics
        {
            public int Renders { get; set; }

            public (double sum, int count) LatencyAverage { get; set; }

            public List<double> LatencyPerRender { get; set; }
        }
    }
}
