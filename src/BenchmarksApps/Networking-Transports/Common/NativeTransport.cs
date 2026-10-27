using System.Net.Transport;

internal static class NativeTransport
{
    public static void Use(IWebHostBuilder host, ServerSettings settings, NetworkEngineOptions engine)
    {
        engine.WorkerCount = settings.Workers;
        engine.WorkerProcessors = settings.Cpus ?? DefaultProcessors(engine, settings.Workers);

        var tls = settings.Tls
            ? new NetworkNativeTlsOptions { CertificatePath = settings.CertificatePath, PrivateKeyPath = settings.PrivateKeyPath }
            : null;

        host.UseNetworkTransport(engine, tls);
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
