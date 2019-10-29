using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Ignitor;
using Microsoft.AspNetCore.SignalR.Client;

namespace BenchmarksClient.Workers
{
    public partial class BlazorIgnitor
    {
        private Task BlazorBasic(CancellationToken cancellationToken)
        {
            var tasks = new Task[_clients.Count];
            var serverUri = new Uri(_job.ServerBenchmarkUri);
            for (var i = 0; i < _clients.Count; i++)
            {
                var client = _clients[i];
                var clientStats = _clientStatistics[i];

                cancellationToken.Register(() =>
                {
                    client.Cancel();
                });

                client.OnCircuitError += error =>
                {
                    client.Cancel();
                    throw new InvalidOperationException($"Received circuit error {error}");
                };

                tasks[i] = Task.Run(async () =>
                {
                    var pizzaOrders = client.FindElementById("pizzaOrders");
                    var count = ReadIntAttribute(pizzaOrders, "pizzaCount");

                    while (!cancellationToken.IsCancellationRequested)
                    {
                        await ComputeStats(clientStats, () => client.ClickAsync("pizza5"), cancellationToken);

                        await ComputeStats(clientStats, () => client.ClickAsync("confirm"), cancellationToken);

                        var newCount = ReadIntAttribute(pizzaOrders, "pizzaCount");
                        if (newCount != count + 1)
                        {
                            throw new InvalidOperationException($"Expected count to be {(count + 1)} but was {newCount}.");
                        }
                        count = newCount;
                    }
                }, cancellationToken);
            }

            return AwaitCancellableTasks(tasks);

            static int ReadIntAttribute(ElementNode currentCount, string attributeName)
            {
                if (!currentCount.Attributes.TryGetValue(attributeName, out var value))
                {
                    throw new Exception($"{attributeName} attribute is missing");
                }

                return int.Parse(value.ToString());
            }
        }

        private Task BlazorFormInput(CancellationToken cancellationToken)
        {
            var tasks = new Task[_clients.Count];
            var serverUri = new Uri(_job.ServerBenchmarkUri);
            for (var i = 0; i < _clients.Count; i++)
            {
                var client = _clients[i];
                var clientStats = _clientStatistics[i];

                cancellationToken.Register(() =>
                {
                    client.Cancel();
                });

                client.OnCircuitError += error =>
                {
                    client.Cancel();
                    throw new InvalidOperationException($"Received circuit error {error}");
                };

                tasks[i] = Task.Run(async () =>
                {
                    await client.ExpectRenderBatch(() => NavigateAsync(client, "checkout", cancellationToken));
                    var element = client.FindElementById("Line1");

                    var inputIndex = 0;

                    while (!cancellationToken.IsCancellationRequested)
                    {
                        var changeEventHandler = element.Events["change"];
                        var value = $"Some text {inputIndex++}";
                        var changeEvent = new
                        {
                            browserRendererId = 0,
                            eventHandlerId = changeEventHandler.EventId,
                            eventArgsType = "change",
                            eventFieldInfo = new
                            {
                                componentId = 0,
                                fieldValue = value,
                            },
                        };

                        var eventArgs = new { type = "change", value = value };

                        await ComputeStats(clientStats,
                            () => client.ExpectRenderBatch(
                                () => client.HubConnection.InvokeAsync(
                                    "DispatchBrowserEvent",
                                    JsonSerializer.Serialize(changeEvent),
                                    JsonSerializer.Serialize(eventArgs))),
                            cancellationToken);

                        var elementValue = element.Attributes["value"].ToString();

                        if (elementValue != value)
                        {
                            throw new InvalidOperationException($"Expected value to be '{value}' but was '{elementValue}'.");
                        }
                    }
                }, cancellationToken);
            }

            return AwaitCancellableTasks(tasks);
        }

        private Task BlazorBackgroundUpdates(CancellationToken cancellationToken)
        {
            var tasks = new Task[_clients.Count];
            var serverUri = new Uri(_job.ServerBenchmarkUri);
            for (var i = 0; i < _clients.Count; i++)
            {
                var client = _clients[i];
                var clientStats = _clientStatistics[i];

                cancellationToken.Register(() =>
                {
                    client.Cancel();
                });

                client.OnCircuitError += error =>
                {
                    client.Cancel();
                    throw new InvalidOperationException($"Received circuit error {error}");
                };

                tasks[i] = Task.Run(async () =>
                {
                    await ComputeStats(
                        clientStats,
                        () => client.ExpectRenderBatch(() => NavigateAsync(client, "orderstatus", cancellationToken)),
                        cancellationToken);
                    var element = client.FindElementById("status");
                    var location = string.Empty;

                    while (!cancellationToken.IsCancellationRequested)
                    {
                        await client.PrepareForNextBatch(timeout: null);
                        if (!element.Attributes.TryGetValue("data-loc", out var newLocation))
                        {
                            throw new InvalidOperationException("Could not find attribute data-loc on element orderstatus");
                        }

                        if (newLocation.ToString() == location)
                        {
                            throw new InvalidOperationException("New location hasn't updated.");
                        }

                        newLocation = location;
                    }
                }, cancellationToken);
            }

            return AwaitCancellableTasks(tasks);
        }

        async Task ComputeStats(ClientStatistics clientStatistics, Func<Task> action, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var startTime = DateTime.UtcNow;
            await action();

            var latency = DateTime.UtcNow - startTime;

            if (_detailedLatency)
            {
                clientStatistics.LatencyPerRender.Add(latency.TotalMilliseconds);
            }
            else
            {
                var (sum, count) = clientStatistics.LatencyAverage;
                sum += latency.TotalMilliseconds;
                count++;
                clientStatistics.LatencyAverage = (sum, count);
            }

            clientStatistics.Renders++;
        }

        private static async Task AwaitCancellableTasks(Task[] tasks)
        {
            try
            {
                await Task.WhenAll(tasks);
            }
            catch (OperationCanceledException)
            {
                // No-op
            }
        }

        Task NavigateAsync(BlazorClient client, string url, CancellationToken cancellationToken)
        {
            return client.HubConnection.InvokeAsync("OnLocationChanged", $"{_job.ServerBenchmarkUri}/{url}", false, cancellationToken);
        }
    }
}
