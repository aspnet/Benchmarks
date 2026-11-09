using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace NetworkingTransports;

// Opt-in memory probe: with NETWORKPROTO_MEMPORT=<port> the process answers "GET /mem[?gc=1]" on a plain loopback listener that is
// independent of the transport under test. gc=1 first runs a blocking, compacting, aggressive GC so that the managed heap is current.
internal static class MemoryDiagnostics
{
    private static readonly string[] s_statusKeys = ["VmRSS", "RssAnon", "RssFile", "RssShmem", "VmLck", "VmPin", "VmData", "VmSwap", "Threads"];
    private static readonly string[] s_smapsKeys = ["Rss", "Pss", "Private_Dirty", "Anonymous", "Locked"];
    private static readonly string[] s_meminfoKeys = ["MemTotal", "MemFree", "MemAvailable", "Slab", "SReclaimable", "SUnreclaim", "KernelStack", "PageTables", "Mlocked", "Unevictable", "Percpu", "Shmem", "AnonPages", "Mapped", "Cached", "Buffers"];

    // Set by ServerHost so that "GET /mem?shutdown=1" can stop the host gracefully and let transports print their end-of-run summary.
    public static Action? StopHost { get; set; }

    public static void StartIfRequested()
    {
        if (!int.TryParse(Environment.GetEnvironmentVariable("NETWORKPROTO_MEMPORT"), out var port))
        {
            return;
        }

        var listener = new TcpListener(IPAddress.Loopback, port);
        listener.Start();
        _ = Task.Run(() => AcceptLoopAsync(listener));
    }

    private static async Task AcceptLoopAsync(TcpListener listener)
    {
        while (true)
        {
            var client = await listener.AcceptTcpClientAsync();
            _ = Task.Run(() => HandleAsync(client));
        }
    }

    private static async Task HandleAsync(TcpClient client)
    {
        using (client)
        {
            try
            {
                var stream = client.GetStream();
                var buffer = new byte[2048];
                var read = await stream.ReadAsync(buffer);
                var request = Encoding.ASCII.GetString(buffer, 0, read);
                if (request.Contains("shutdown=1", StringComparison.Ordinal))
                {
                    await stream.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"));
                    StopHost?.Invoke();
                    return;
                }

                if (request.Contains("gc=1", StringComparison.Ordinal))
                {
                    GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
                    GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
                    GC.WaitForPendingFinalizers();
                    GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
                }

                var body = Encoding.UTF8.GetBytes(Snapshot());
                var header = Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
                await stream.WriteAsync(header);
                await stream.WriteAsync(body);
            }
            catch (Exception)
            {
            }
        }
    }

    private static string Snapshot()
    {
        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();
            json.WriteString("os", RuntimeInformation.OSDescription);
            json.WriteNumber("tickMs", Environment.TickCount64);

            var info = GC.GetGCMemoryInfo(GCKind.Any);
            json.WriteStartObject("gc");
            json.WriteNumber("totalMemory", GC.GetTotalMemory(false));
            json.WriteNumber("allocatedBytes", GC.GetTotalAllocatedBytes(false));
            json.WriteNumber("heapSize", info.HeapSizeBytes);
            json.WriteNumber("committed", info.TotalCommittedBytes);
            json.WriteNumber("fragmented", info.FragmentedBytes);
            json.WriteNumber("pinnedObjects", info.PinnedObjectsCount);
            json.WriteNumber("collections0", GC.CollectionCount(0));
            json.WriteNumber("collections2", GC.CollectionCount(2));
            json.WriteStartArray("generationSizeAfter");
            foreach (var generation in info.GenerationInfo)
            {
                json.WriteNumberValue(generation.SizeAfterBytes);
            }

            json.WriteEndArray();
            json.WriteEndObject();

            using var process = Process.GetCurrentProcess();
            json.WriteStartObject("process");
            json.WriteNumber("workingSet", process.WorkingSet64);
            json.WriteNumber("privateMemory", process.PrivateMemorySize64);
            json.WriteNumber("virtualMemory", process.VirtualMemorySize64);
            json.WriteNumber("threads", process.Threads.Count);
            json.WriteNumber("handles", process.HandleCount);
            json.WriteNumber("poolThreads", ThreadPool.ThreadCount);
            if (OperatingSystem.IsWindows())
            {
                json.WriteNumber("nonpagedPool", process.NonpagedSystemMemorySize64);
                json.WriteNumber("pagedPool", process.PagedSystemMemorySize64);
                json.WriteNumber("pagedMemory", process.PagedMemorySize64);
            }

            json.WriteEndObject();

            if (OperatingSystem.IsWindows())
            {
                WriteWindowsSystem(json);
            }
            else if (OperatingSystem.IsLinux())
            {
                WriteLinux(json);
            }

            json.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static void WriteWindowsSystem(Utf8JsonWriter json)
    {
        var info = new PerformanceInformation { Size = Marshal.SizeOf<PerformanceInformation>() };
        if (!GetPerformanceInfo(out info, info.Size))
        {
            return;
        }

        var page = (long)info.PageSize;
        json.WriteStartObject("system");
        json.WriteNumber("commitTotal", (long)info.CommitTotal * page);
        json.WriteNumber("physicalAvailable", (long)info.PhysicalAvailable * page);
        json.WriteNumber("systemCache", (long)info.SystemCache * page);
        json.WriteNumber("kernelPaged", (long)info.KernelPaged * page);
        json.WriteNumber("kernelNonpaged", (long)info.KernelNonpaged * page);
        json.WriteEndObject();
    }

    private static void WriteLinux(Utf8JsonWriter json)
    {
        WriteKeyedKb(json, "status", "/proc/self/status", s_statusKeys);
        WriteKeyedKb(json, "smaps", "/proc/self/smaps_rollup", s_smapsKeys);
        WriteKeyedKb(json, "meminfo", "/proc/meminfo", s_meminfoKeys);

        json.WriteStartObject("sockstat");
        foreach (var line in ReadLines("/proc/net/sockstat"))
        {
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3 || parts[0] != "TCP:")
            {
                continue;
            }

            for (var i = 1; i + 1 < parts.Length; i += 2)
            {
                if (long.TryParse(parts[i + 1], out var value))
                {
                    json.WriteNumber(parts[i], value);
                }
            }
        }

        json.WriteEndObject();
    }

    private static void WriteKeyedKb(Utf8JsonWriter json, string section, string path, string[] keys)
    {
        json.WriteStartObject(section);
        foreach (var line in ReadLines(path))
        {
            var colon = line.IndexOf(':');
            if (colon <= 0)
            {
                continue;
            }

            var key = line[..colon];
            if (Array.IndexOf(keys, key) < 0)
            {
                continue;
            }

            var rest = line.AsSpan(colon + 1).Trim();
            var space = rest.IndexOf(' ');
            var number = space < 0 ? rest : rest[..space];
            if (long.TryParse(number, out var value))
            {
                var isKb = rest.EndsWith("kB", StringComparison.Ordinal);
                json.WriteNumber(key, isKb ? value * 1024 : value);
            }
        }

        json.WriteEndObject();
    }

    private static IEnumerable<string> ReadLines(string path)
    {
        try
        {
            return File.ReadAllLines(path);
        }
        catch (IOException)
        {
            return [];
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PerformanceInformation
    {
        public int Size;
        public nuint CommitTotal;
        public nuint CommitLimit;
        public nuint CommitPeak;
        public nuint PhysicalTotal;
        public nuint PhysicalAvailable;
        public nuint SystemCache;
        public nuint KernelTotal;
        public nuint KernelPaged;
        public nuint KernelNonpaged;
        public nuint PageSize;
        public int HandleCount;
        public int ProcessCount;
        public int ThreadCount;
    }

    [DllImport("psapi.dll", SetLastError = true)]
    private static extern bool GetPerformanceInfo(out PerformanceInformation info, int size);
}
