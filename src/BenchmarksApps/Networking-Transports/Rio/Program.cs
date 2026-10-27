using System.Net.Transport;

await ServerHost.RunAsync("rio", args, (host, settings) => NativeTransport.Use(host, settings, new RioEngineOptions()));
