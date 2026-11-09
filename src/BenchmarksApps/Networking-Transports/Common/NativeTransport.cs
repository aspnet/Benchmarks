using System.Net.Transport;

internal static class NativeTransport
{
    public static void Use(IWebHostBuilder host, ServerSettings settings, NetworkEngineOptions engine)
    {
        engine.WorkerCount = settings.Workers;
        engine.WorkerProcessors = settings.Cpus ?? DefaultProcessors(engine, settings.Workers);
        ConfigureWindowsPools(engine);

        var tls = settings.Tls && settings.Layer == NetworkingTransports.TlsLayerKind.Native
            ? new NetworkNativeTlsOptions { CertificatePath = settings.CertificatePath, PrivateKeyPath = settings.PrivateKeyPath }
            : null;

        // NETWORKPROTO_REGISTRATION=bedrock registers the shared transport as the Microsoft.AspNetCore.Connections IConnectionListenerFactory; the default keeps the original Kestrel-specific factory.
        if (string.Equals(Environment.GetEnvironmentVariable("NETWORKPROTO_REGISTRATION"), "bedrock", StringComparison.OrdinalIgnoreCase))
        {
            host.ConfigureServices(services => services.AddNetworkTransport(engine, tls));
        }
        else
        {
            host.UseNetworkTransport(engine, tls);
        }
    }

    // NETWORKPROTO_WINDOWS_POOL_IN_MB / _OUT_MB / _TRIM_SECONDS override the Windows per-worker pool caps and the idle-slab trim delay (TRIM_SECONDS=0 disables trimming).
    private static void ConfigureWindowsPools(NetworkEngineOptions engine)
    {
        if (engine is not WindowsEngineOptions windows)
        {
            return;
        }
        if (int.TryParse(Environment.GetEnvironmentVariable("NETWORKPROTO_WINDOWS_POOL_IN_MB"), out var inputMb))
        {
            windows.InputPoolMaximumBytes = inputMb * 1024 * 1024;
        }
        if (int.TryParse(Environment.GetEnvironmentVariable("NETWORKPROTO_WINDOWS_POOL_OUT_MB"), out var outputMb))
        {
            windows.OutputPoolMaximumBytes = outputMb * 1024 * 1024;
        }
        if (double.TryParse(Environment.GetEnvironmentVariable("NETWORKPROTO_WINDOWS_TRIM_SECONDS"), out var trimSeconds))
        {
            windows.PoolIdleTrimDelay = trimSeconds <= 0 ? Timeout.InfiniteTimeSpan : TimeSpan.FromSeconds(trimSeconds);
        }
    }

    // The prototype pins io_uring worker i to CPU 2*i (SMT siblings) when no list is given,
    // which is invalid on small VMs (e.g. 4 vCPUs with 4 workers), so fall back to 0..workers-1.
    private static int[]? DefaultProcessors(NetworkEngineOptions engine, int workers)
    {
        return engine is IoUringEngineOptions && Environment.ProcessorCount < 2 * workers
            ? Enumerable.Range(0, workers).ToArray()
            : null;
    }
}
