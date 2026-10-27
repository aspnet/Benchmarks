using System.Net.Transport;

await ServerHost.RunAsync("iocp", args, (host, settings) => NativeTransport.Use(host, settings, new IocpEngineOptions()));
