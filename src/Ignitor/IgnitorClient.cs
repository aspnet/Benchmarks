using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR.Client;

namespace Ignitor
{
    internal class BlazorClient
    {
        private readonly object @lock = new object();
        private readonly List<PredicateItem> predicates = new List<PredicateItem>();
        private readonly string baseUrl;
        public int Requests;
        public List<double> LatencyPerConnection = new List<double>();
        public (double sum, int count) LatencyAverage;
        public int BadResponses;

        public BlazorClient(HubConnection connection, string baseUrl)
        {
            HubConnection = connection;
            this.baseUrl = baseUrl;
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
            await HubConnection.InvokeAsync<string>("StartCircuit", baseUrl, baseUrl);
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

            var argsObject = new object[] { $"{baseUrl}/{href}", true };
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
