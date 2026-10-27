using System.Net.Transport;

await ServerHost.RunAsync("io_uring", args, (host, settings) => NativeTransport.Use(host, settings, new IoUringEngineOptions()));
