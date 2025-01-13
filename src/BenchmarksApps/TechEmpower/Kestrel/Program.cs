using System.Runtime.InteropServices;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Transport.Sockets;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

var loggerFactory = new NullLoggerFactory();
var socketOptions = new SocketTransportOptions()
{
    WaitForDataBeforeAllocatingBuffer = false,
    UnsafePreferInlineScheduling = RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
                                   && Environment.GetEnvironmentVariable("DOTNET_SYSTEM_NET_SOCKETS_INLINE_COMPLETIONS") == "1"
};
if (int.TryParse(Environment.GetEnvironmentVariable("threadCount"), out var value))
{
    socketOptions.IOQueueCount = value;
}
using var server = new KestrelServer(
    Options.Create(new KestrelServerOptions()),
    new SocketTransportFactory(Options.Create(socketOptions), loggerFactory),
    loggerFactory
    );

await server.StartAsync(new BenchmarkApp(), CancellationToken.None);

var addresses = server.Features.GetRequiredFeature<IServerAddressesFeature>().Addresses;
foreach (var address in addresses)
{
    Console.WriteLine($"Now listening on: {address}");
}

using var lifetime = new ConsoleLifetime();
await lifetime.LifetimeTask;

Console.Write("Server shutting down...");
await server.StopAsync(CancellationToken.None);
Console.Write(" done.");
