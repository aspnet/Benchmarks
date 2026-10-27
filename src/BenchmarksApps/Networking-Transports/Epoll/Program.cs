using System.Net.Transport;

await ServerHost.RunAsync("epoll", args, (host, settings) => NativeTransport.Use(host, settings, new EpollEngineOptions()));
