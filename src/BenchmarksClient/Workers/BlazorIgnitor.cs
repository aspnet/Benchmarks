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
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace BenchmarksClient.Workers
{
    public partial class BlazorIgnitor : IWorker
    {
        private ClientJob _job;
        private HttpClientHandler _httpClientHandler;
        private List<BlazorClient> _clients;
        private List<IDisposable> _recvCallbacks;
        private Stopwatch _workTimer = new Stopwatch();
        private bool _stopped;
        private SemaphoreSlim _lock = new SemaphoreSlim(1);
        private bool _detailedLatency;
        private string _scenario;
        private int _totalRequests;
        private HttpClient _httpClient;
        private CancellationTokenSource _cancelationTokenSource;

        public string JobLogText { get; set; }

        private Task InitializeJob()
        {
            _stopped = false;

            _detailedLatency = _job.Latency == null;

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

            jobLogText += "]";
            JobLogText = jobLogText;
            if (_clients == null)
            {
                CreateConnections();
            }

            return Task.CompletedTask;
        }

        public async Task StartJobAsync(ClientJob job)
        {
            _job = job;
            Log($"Starting Job");
            await InitializeJob();
            // start connections
            var tasks = new List<Task>(_clients.Count);
            foreach (var item in _clients)
            {
                tasks.Add(item.InitializeAsync(default));
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

                    case "Rogue":
                        await Rogue(_cancelationTokenSource.Token);
                        break;


                    //case "Reconnects":
                    //    await Reconnects(_cancelationTokenSource.Token);
                    //    break;

                    case "BlazingPizza":
                        await BlazingPizza(_cancelationTokenSource.Token);
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
            foreach (var callback in _recvCallbacks)
            {
                // stops stat collection from happening quicker than StopAsync
                // and we can do all the calculations while close is occurring
                callback.Dispose();
            }

            // stop connections
            Log("Stopping connections");
            var tasks = new List<Task>(_clients.Count);
            foreach (var item in _clients)
            {
                tasks.Add(item.DisposeAsync());
            }

            await Task.WhenAll(tasks);
            Log("Connections have been disposed");

            _httpClientHandler.Dispose();
            _httpClient.Dispose();
            // TODO: Remove when clients no longer take a long time to "cool down"
            await Task.Delay(5000);

            Log("Stopped worker");
        }

        public void Dispose()
        {
            var tasks = new List<Task>(_clients.Count);
            foreach (var item in _clients)
            {
                tasks.Add(item.DisposeAsync());
            }

            Task.WhenAll(tasks).GetAwaiter().GetResult();

            _httpClientHandler.Dispose();
        }

        private void CreateConnections()
        {
            _clients = new List<BlazorClient>(_job.Connections);


            _httpClient = new HttpClient { BaseAddress = new Uri(_job.ServerBenchmarkUri) };

            _recvCallbacks = new List<IDisposable>(_job.Connections);
            for (var i = 0; i < _job.Connections; i++)
            {
                var connection = CreateHubConnection();
                _clients.Add(new BlazorClient(connection) { HttpClient = _httpClient });
            }
        }

        private HubConnection CreateHubConnection()
        {
            var baseUri = new Uri(_job.ServerBenchmarkUri);
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
            return connection;
        }

        private void CalculateStatistics()
        {
            // RPS
            var newTotalRequests = 0;
            var min = 0;
            var max = 0;
            foreach (var client in _clients)
            {
                newTotalRequests += client.Requests;
                max = Math.Max(max, client.Requests);
                min = Math.Max(min, client.Requests);
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
                foreach (var client in _clients)
                {
                    totalCount += client.LatencyPerConnection.Count;
                    totalSum += client.LatencyPerConnection.Sum();

                    allConnections.AddRange(client.LatencyPerConnection);
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
                foreach (var client in _clients)
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

        private class BlazorClient
        {
            private readonly object @lock = new object();
            private readonly List<PredicateItem> predicates = new List<PredicateItem>();
            public int Requests;
            public List<double> LatencyPerConnection = new List<double>();
            public (double sum, int count) LatencyAverage;
            public int BadResponses;

            public BlazorClient(HubConnection connection)
            {
                HubConnection = connection;
            }

            public bool DetailedLatency { get; set; }

            public HubConnection HubConnection { get; }

            public ElementHive ElementHive { get; } = new ElementHive();

            public HttpClient HttpClient { get; set; }

            public Task WaitUntil(Func<ElementHive, bool> predicate, TimeSpan? timeout = null)
            {
                lock (@lock)
                {
                    // Perhaps the predicate is already true
                    if (predicate(ElementHive))
                    {
                        return Task.CompletedTask;
                    }

                    var tcs = new TaskCompletionSource<int>();
                    var cancellationTokenSource = new CancellationTokenSource();
                    predicates.Add(new PredicateItem(predicate, tcs, cancellationTokenSource));

                    return Task.WhenAny(tcs.Task, TimeoutTask());

                    async Task TimeoutTask()
                    {
                        timeout ??= Debugger.IsAttached ? TimeSpan.FromMilliseconds(-1) : TimeSpan.FromSeconds(5);

                        await Task.Delay(timeout.Value, cancellationTokenSource.Token);
                        throw new TimeoutException("Waiting for predicate timed out");
                    }
                }
            }

            public Task InitializeAsync(CancellationToken cancellationToken) => HubConnection.StartAsync(cancellationToken);

            public async Task RunAsync(CancellationToken cancellationToken)
            {
                var dateTime = DateTime.UtcNow;

                HubConnection.On<int, int, byte[]>("JS.RenderBatch", OnRenderBatch);
                await ConnectAsync(cancellationToken);

                async Task OnRenderBatch(int browserRendererId, int batchId, byte[] batchData)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        return;
                    }

                    Requests++;
                    AddLatency(ref dateTime);

                    try
                    {
                        await HubConnection.SendAsync("OnRenderCompleted", batchId, null, cancellationToken);

                        var batch = RenderBatchReader.Read(batchData);

                        lock (@lock)
                        {
                            ElementHive.Update(batch);

                            for (var i = predicates.Count - 1; i >= 0; i--)
                            {
                                var item = predicates[i];
                                if (item.Predicate(ElementHive))
                                {
                                    item.CancellationTokenSource.Cancel();
                                    item.TaskCompletionSource.TrySetResult(0);
                                    predicates.RemoveAt(i);
                                    break;
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        foreach (var item in predicates)
                        {
                            item.TaskCompletionSource.TrySetException(ex);
                        }
                    }
                }
            }

            void AddLatency(ref DateTime previousTime)
            {
                var latency = DateTime.UtcNow - previousTime;
                if (DetailedLatency)
                {
                    LatencyPerConnection.Add(latency.TotalMilliseconds);
                }
                else
                {
                    var (sum, count) = LatencyAverage;
                    sum += latency.TotalMilliseconds;
                    count++;
                    LatencyAverage = (sum, count);
                }

                previousTime = DateTime.UtcNow;
            }

            public async Task ConnectAsync(CancellationToken cancellationToken)
            {
                await HubConnection.InvokeAsync<string>("StartCircuit", "http://example.com/app", "http://example.com");
            }

            async Task ConnectToPrerenderCircuitAsync(CancellationToken cancellationToken)
            {
                for (var i = 0; i < 5; i++)
                {
                    using var response = await HttpClient.GetAsync("");
                    var content = await response.Content.ReadAsStringAsync();

                    // <!-- M.A.C.Component:{"circuitId":"CfDJ8KZCIaqnXmdF...PVd6VVzfnmc1","rendererId":"0","componentId":"0"} -->
                    var match = Regex.Match(content, $"{Regex.Escape("<!-- M.A.C.Component:")}(.+?){Regex.Escape(" -->")}");
                    using var json = JsonDocument.Parse(match.Groups[1].Value);
                    var circuitId = json.RootElement.GetProperty("circuitId").GetString();

                    var success = await HubConnection.InvokeAsync<bool>("ConnectCircuit", circuitId, cancellationToken);
                    if (success)
                    {
                        return;
                    }

                    if (!success)
                    {
                        BadResponses++;
                        // Retry after a short delay
                        await Task.Delay(i * 250);
                    }
                }

                throw new InvalidOperationException("ConnectCircuit failed");
            }

            public async Task DisposeAsync()
            {
                foreach (var item in predicates)
                {
                    item.TaskCompletionSource.TrySetCanceled();
                    item.CancellationTokenSource.Cancel();
                }

                await HubConnection.DisposeAsync();
            }

            public Task NavigateTo(string href, CancellationToken cancellationToken)
            {
                var assemblyName = "Microsoft.AspNetCore.Components.Server";
                var methodIdentifier = "NotifyLocationChanged";

                var argsObject = new object[] { $"{HttpClient.BaseAddress}/{href}", true };
                var locationChangedArgs = JsonSerializer.Serialize(argsObject, new JsonSerializerOptions() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
                return HubConnection.SendAsync("BeginInvokeDotNetFromJS", "0", assemblyName, methodIdentifier, 0, locationChangedArgs, cancellationToken);
            }

            private readonly struct PredicateItem
            {
                public PredicateItem(Func<ElementHive, bool> predicate, TaskCompletionSource<int> tcs, CancellationTokenSource cancellationTokenSource)
                {
                    Predicate = predicate;
                    TaskCompletionSource = tcs;
                    CancellationTokenSource = cancellationTokenSource;
                }

                public Func<ElementHive, bool> Predicate { get; }

                public TaskCompletionSource<int> TaskCompletionSource { get; }

                public CancellationTokenSource CancellationTokenSource { get; }
            }
        }
    }
}
