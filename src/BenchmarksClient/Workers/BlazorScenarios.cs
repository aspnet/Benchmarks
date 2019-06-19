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
        private async Task Navigator(CancellationToken cancellationToken)
        {
            var random = new Random();
            var links = new[] { "home", "fetchdata", "counter", "ticker" };

            for (var j = 0; j < _connections.Count; j++)
            {
                var i = j;
                var hive = new ElementHive();
                var item = _connections[i];

                var connection = item.HubConnection;
                var circuitId = item.CircuitId;
                var uri = item.BaseUri;

                await Task.Run(async () =>
                {
                    var tcs = new TaskCompletionSource<int>();
                    var dateTime = DateTime.UtcNow;

                    connection.On("JS.RenderBatch", async (int browserRendererId, int batchId, byte[] batchData) =>
                    {
                        _requestsPerConnection[i] += 1;
                        AddLatency(i, dateTime);

                        if (cancellationToken.IsCancellationRequested)
                        {
                            tcs.TrySetCanceled(cancellationToken);
                            return;
                        }

                        try
                        {
                            var batch = RenderBatchReader.Read(batchData);
                            hive.Update(batch);

                            await connection.SendAsync("OnRenderCompleted", batchId, null, cancellationToken);
                            dateTime = DateTime.UtcNow;
                            await NavigateTo(connection, links[random.Next(0, links.Length)], cancellationToken);
                        }
                        catch (Exception ex)
                        {
                            _job.BadResponses++;
                            await connection.SendAsync("OnRenderCompleted", batchId, ex.Message, cancellationToken);

                            tcs.TrySetException(ex);
                            throw;
                        }
                    });


                    await ConnectCircuit(connection, circuitId, cancellationToken);

                    await Task.WhenAny(Task.Delay(_job.Duration), tcs.Task);
                });
            }
        }

        private async Task Clicker(CancellationToken cancellationToken)
        {
            var tasks = new Task[_connections.Count];
            for (var j = 0; j < _connections.Count; j++)
            {
                var i = j;
                var hive = new ElementHive();
                var item = _connections[i];

                var connection = item.HubConnection;
                var circuitId = item.CircuitId;

                tasks[i] = Task.Run(async () =>
                {
                    ElementNode counter = null;
                    var tcs = new TaskCompletionSource<int>();
                    var dateTime = DateTime.UtcNow;

                    connection.On("JS.RenderBatch", async (int browserRendererId, int batchId, byte[] batchData) =>
                    {
                        _requestsPerConnection[i] += 1;
                        AddLatency(i, dateTime);

                        try
                        {
                            if (cancellationToken.IsCancellationRequested)
                            {
                                tcs.TrySetCanceled(cancellationToken);
                                return;
                            }

                            await connection.SendAsync("OnRenderCompleted", batchId, null, cancellationToken);

                            var batch = RenderBatchReader.Read(batchData);
                            hive.Update(batch);

                            if (counter == null && !hive.TryFindElementById("counter", out counter))
                            {

                                // Nothing to do until we've rendered counter.
                                return;
                            }

                            dateTime = DateTime.UtcNow;
                            await counter.ClickAsync(connection);
                        }
                        catch (Exception ex)
                        {
                            _job.BadResponses++;
                            await connection.SendAsync("OnRenderCompleted", batchId, ex.Message, cancellationToken);

                            tcs.TrySetException(ex);
                            throw;
                        }
                    });

                    await ConnectCircuit(connection, circuitId, cancellationToken);
                    await NavigateTo(connection, "counter", cancellationToken);

                    await Task.WhenAny(Task.Delay(_job.Duration), tcs.Task);
                });
            }

            await Task.WhenAll(tasks);
        }

        void AddLatency(int connectionId, DateTime startTime)
        {
            var latency = DateTime.UtcNow - startTime;
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

        private async Task Reconnects(CancellationToken cancellationToken)
        {
            int reconnectDelay = 500;
            if (_job.ClientProperties.TryGetValue("ReconnectDelay", out var value))
            {
                reconnectDelay = int.Parse(value);
            }

            var tasks = new Task[_connections.Count];

            for (var i = 0; i < _connections.Count; i++)
            {
                var index = i;
                var hive = new ElementHive();
                var item = _connections[i];

                var connection = item.HubConnection;
                var circuitId = item.CircuitId;

                tasks[i] = Task.Run(async () =>
                {
                    var tcs = new TaskCompletionSource<int>();

                    connection.On("JS.RenderBatch", async (int browserRendererId, int batchId, byte[] batchData) =>
                    {
                        try
                        {
                            if (cancellationToken.IsCancellationRequested)
                            {
                                tcs.TrySetCanceled(cancellationToken);
                                return;
                            }

                            var batch = RenderBatchReader.Read(batchData);
                            hive.Update(batch);

                            await connection.SendAsync("OnRenderCompleted", batchId, null, cancellationToken);
                        }
                        catch (Exception ex)
                        {
                            _job.BadResponses++;
                            await connection.SendAsync("OnRenderCompleted", batchId, ex.Message, cancellationToken);

                            tcs.TrySetException(ex);
                            throw;
                        }
                    });

                    while (true)
                    {
                        if (connection.State == HubConnectionState.Disconnected)
                        {
                            await connection.StartAsync(cancellationToken);
                        }

                        await ConnectCircuit(connection, circuitId, cancellationToken);

                        _requestsPerConnection[index] += 1;

                        await NavigateTo(connection, "ticker", cancellationToken);
                        if (tcs.Task.IsCompleted)
                        {
                            await tcs.Task;
                        }

                        await Task.Delay(OperationDelay, cancellationToken);
                        await connection.StopAsync(cancellationToken);
                        await Task.Delay(reconnectDelay, cancellationToken);
                    }
                });
            }

            await Task.WhenAll(tasks);
        }

        private async Task ConnectCircuit(HubConnection connection, string circuitId, CancellationToken cancellationToken)
        {
            for (var i = 0; i < 3; i++)
            {
                var success = await connection.InvokeAsync<bool>("ConnectCircuit", circuitId, cancellationToken);
                if (success)
                {
                    return;
                }

                if (!success)
                {
                    _job.BadResponses++;
                    // Retry after a short delay
                    await Task.Delay(1000);
                }
            }

            throw new InvalidOperationException("ConnectCircuit failed");
        }

        async Task NavigateTo(HubConnection connection, string href, CancellationToken cancellationToken)
        {
            var assemblyName = "Microsoft.AspNetCore.Components.Server";
            var methodIdentifier = "NotifyLocationChanged";

            var argsObject = new object[] { $"{_job.ServerBenchmarkUri}/{href}", true };
            var locationChangedArgs = JsonSerializer.ToString(argsObject, new JsonSerializerOptions() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            await connection.InvokeAsync("BeginInvokeDotNetFromJS", "0", assemblyName, methodIdentifier, 0, locationChangedArgs, cancellationToken);
        }
    }
}
