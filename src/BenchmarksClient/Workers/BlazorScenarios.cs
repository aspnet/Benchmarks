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
            for (var i = 0; i < _connections.Count; i++)
            {
                var hive = new ElementHive();
                var item = _connections[i];

                var connection = item.HubConnection;
                var circuitId = item.CircuitId;
                var uri = item.BaseUri;
                var links = new[] { "home", "fetchdata", "counter" };

                await Task.Run(async () =>
                {
                    var tcs = new TaskCompletionSource<int>();
                    var index = 0;

                    connection.On("JS.RenderBatch", async (int browserRendererId, int batchId, byte[] batchData) =>
                    {
                        if (cancellationToken.IsCancellationRequested)
                        {
                            tcs.TrySetCanceled(cancellationToken);
                            return;
                        }

                        try
                        {
                            index = index++ % links.Length;

                            var batch = RenderBatchReader.Read(batchData);
                            hive.Update(batch);

                            await connection.SendAsync("OnRenderCompleted", batchId, null, cancellationToken);

                            await Task.Delay(OperationDelay, cancellationToken);
                            await NavigateTo(connection, uri, cancellationToken);
                        }
                        catch (Exception ex)
                        {
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

            for (var i = 0; i < _connections.Count; i++)
            {
                var hive = new ElementHive();
                var item = _connections[i];

                var connection = item.HubConnection;
                var circuitId = item.CircuitId;

                tasks[i] = Task.Run(async () =>
                {
                    ElementNode counter = null;
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

                            await connection.SendAsync("OnRenderCompleted", batchId, null, cancellationToken);

                            var batch = RenderBatchReader.Read(batchData);
                            hive.Update(batch);

                            if (counter == null && !hive.TryFindElementById("counter", out counter))
                            {

                                // Nothing to do until we've rendered counter.
                                return;
                            }

                            await Task.Delay(OperationDelay);
                            await counter.ClickAsync(connection);
                        }
                        catch (Exception ex)
                        {
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

        private async Task Reconnects(CancellationToken cancellationToken)
        {

            var navigateToTicker = false;
            if (_job.ClientProperties.TryGetValue("NavigateToTicker", out var value))
            {
                navigateToTicker = bool.Parse(value);
            }

            var tasks = new Task[_connections.Count];

            for (var i = 0; i < _connections.Count; i++)
            {
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
                        await NavigateTo(connection, "ticker", cancellationToken);
                        if (tcs.Task.IsCompleted)
                        {
                            await tcs.Task;
                        }

                        await Task.Delay(OperationDelay, cancellationToken);
                        await connection.StopAsync(cancellationToken);
                        await Task.Delay(OperationDelay, cancellationToken);
                    }
                });
            }

            await Task.WhenAll(tasks);
        }

        private static async Task ConnectCircuit(HubConnection connection, string circuitId, CancellationToken cancellationToken)
        {
            var success = await connection.InvokeAsync<bool>("ConnectCircuit", circuitId, cancellationToken);

            if (!success)
            {
                throw new InvalidOperationException("ConnectCircuit failed");
            }
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
