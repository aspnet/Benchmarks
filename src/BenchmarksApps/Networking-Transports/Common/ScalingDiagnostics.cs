using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace NetworkingTransports;

// Opt-in (NETWORKPROTO_DIAG=1, Linux only) scaling probe. Once per second it prints, to stdout (crank shows it in the application log):
//   DIAG_CPU    cpuset-wide user/system/irq/softirq/idle split from /proc/stat, restricted to the CPUs the process may run on
//   DIAG_PERCPU busy percentage of every allowed CPU (every fifth sample)
//   DIAG_THR    CPU time, context switches and run-queue wait of the process threads, grouped by thread name
//   DIAG_RT     runtime counters (thread pool, lock contention, GC)
// Counters that are cumulative are printed as cumulative values; the analysis takes differences.
internal static class ScalingDiagnostics
{
    private const double Hz = 100;

    public static void StartIfRequested()
    {
        if (Environment.GetEnvironmentVariable("NETWORKPROTO_DIAG") != "1" || !OperatingSystem.IsLinux())
        {
            return;
        }

        new Thread(Run) { IsBackground = true, Name = "scaling-diag" }.Start();
    }

    private static void Run()
    {
        var cpus = ReadAllowedCpus();
        var previousCpu = ReadCpuTimes(cpus);
        var previousThreads = new Dictionary<int, ThreadSample>();
        var watch = Stopwatch.StartNew();
        var last = watch.Elapsed;
        var sample = 0;
        ReadThreads(previousThreads, new Dictionary<string, Group>());

        Console.WriteLine($"DIAG_START cpus={cpus.Length} processorCount={Environment.ProcessorCount} tickHz={Hz}");
        while (true)
        {
            Thread.Sleep(1000);
            sample++;
            var now = watch.Elapsed;
            var seconds = (now - last).TotalSeconds;
            last = now;
            var t = (int)now.TotalSeconds;

            var cpu = ReadCpuTimes(cpus);
            WriteCpu(t, cpus, previousCpu, cpu, sample % 5 == 0);
            previousCpu = cpu;

            var groups = new Dictionary<string, Group>();
            var current = new Dictionary<int, ThreadSample>();
            ReadThreads(current, groups, previousThreads);
            previousThreads = current;
            WriteThreads(t, seconds, groups);
            WriteRuntime(t);
        }
    }

    private static int[] ReadAllowedCpus()
    {
        foreach (var line in File.ReadLines("/proc/self/status"))
        {
            if (!line.StartsWith("Cpus_allowed_list:", StringComparison.Ordinal))
            {
                continue;
            }

            var cpus = new List<int>();
            foreach (var part in line["Cpus_allowed_list:".Length..].Trim().Split(','))
            {
                var range = part.Split('-');
                var from = int.Parse(range[0], CultureInfo.InvariantCulture);
                var to = range.Length > 1 ? int.Parse(range[1], CultureInfo.InvariantCulture) : from;
                for (var cpu = from; cpu <= to; cpu++)
                {
                    cpus.Add(cpu);
                }
            }

            return [.. cpus];
        }

        return [];
    }

    // jiffies per allowed cpu: user, nice, system, idle, iowait, irq, softirq, steal
    private static Dictionary<int, long[]> ReadCpuTimes(int[] cpus)
    {
        var allowed = new HashSet<int>(cpus);
        var result = new Dictionary<int, long[]>();
        foreach (var line in File.ReadLines("/proc/stat"))
        {
            if (!line.StartsWith("cpu", StringComparison.Ordinal) || line.Length < 4 || !char.IsDigit(line[3]))
            {
                continue;
            }

            var fields = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var id = int.Parse(fields[0].AsSpan(3), CultureInfo.InvariantCulture);
            if (!allowed.Contains(id))
            {
                continue;
            }

            var values = new long[8];
            for (var i = 0; i < 8; i++)
            {
                values[i] = long.Parse(fields[i + 1], CultureInfo.InvariantCulture);
            }

            result[id] = values;
        }

        return result;
    }

    private static void WriteCpu(int t, int[] cpus, Dictionary<int, long[]> before, Dictionary<int, long[]> after, bool perCpu)
    {
        var total = new long[8];
        var perCpuText = new StringBuilder();
        foreach (var cpu in cpus)
        {
            if (!before.TryGetValue(cpu, out var b) || !after.TryGetValue(cpu, out var a))
            {
                continue;
            }

            long sum = 0, idle = 0;
            for (var i = 0; i < 8; i++)
            {
                var delta = a[i] - b[i];
                total[i] += delta;
                sum += delta;
                if (i is 3 or 4)
                {
                    idle += delta;
                }
            }

            perCpuText.Append(sum == 0 ? 0 : (int)Math.Round(100.0 * (sum - idle) / sum)).Append(' ');
        }

        double all = total.Sum();
        if (all <= 0)
        {
            return;
        }

        string P(long v) => (100.0 * v / all).ToString("0.0", CultureInfo.InvariantCulture);
        Console.WriteLine($"DIAG_CPU t={t} user={P(total[0] + total[1])} sys={P(total[2])} irq={P(total[5])} softirq={P(total[6])} iowait={P(total[4])} steal={P(total[7])} idle={P(total[3])}");
        if (perCpu)
        {
            Console.WriteLine($"DIAG_PERCPU t={t} {perCpuText.ToString().TrimEnd()}");
        }
    }

    private sealed class Group
    {
        public int Threads;
        public long User, System, Voluntary, Involuntary, WaitNs;
    }

    private readonly record struct ThreadSample(long User, long System, long Voluntary, long Involuntary, long WaitNs);

    private static string GroupName(string comm)
    {
        if (comm.StartsWith("Epoll", StringComparison.Ordinal))
        {
            return "Epoll";
        }

        if (comm.StartsWith("Managed io_u", StringComparison.Ordinal))
        {
            return "IoUring";
        }

        if (comm.StartsWith(".NET TP Worker", StringComparison.Ordinal))
        {
            return "TPWorker";
        }

        return comm.StartsWith(".NET", StringComparison.Ordinal) ? comm.Replace(' ', '_') : "other";
    }

    private static void ReadThreads(Dictionary<int, ThreadSample> current, Dictionary<string, Group> groups, Dictionary<int, ThreadSample>? previous = null)
    {
        foreach (var directory in Directory.EnumerateDirectories("/proc/self/task"))
        {
            if (!int.TryParse(Path.GetFileName(directory), out var tid))
            {
                continue;
            }

            try
            {
                var stat = File.ReadAllText(directory + "/stat");
                var open = stat.IndexOf('(');
                var close = stat.LastIndexOf(')');
                var comm = stat[(open + 1)..close];
                var fields = stat[(close + 2)..].Split(' ');
                var user = long.Parse(fields[11], CultureInfo.InvariantCulture);
                var system = long.Parse(fields[12], CultureInfo.InvariantCulture);
                long voluntary = 0, involuntary = 0, waitNs = 0;
                foreach (var line in File.ReadLines(directory + "/status"))
                {
                    if (line.StartsWith("voluntary_ctxt_switches:", StringComparison.Ordinal))
                    {
                        voluntary = long.Parse(line.AsSpan("voluntary_ctxt_switches:".Length).Trim(), CultureInfo.InvariantCulture);
                    }
                    else if (line.StartsWith("nonvoluntary_ctxt_switches:", StringComparison.Ordinal))
                    {
                        involuntary = long.Parse(line.AsSpan("nonvoluntary_ctxt_switches:".Length).Trim(), CultureInfo.InvariantCulture);
                    }
                }

                var schedstat = File.ReadAllText(directory + "/schedstat").Split(' ');
                if (schedstat.Length >= 2)
                {
                    waitNs = long.Parse(schedstat[1], CultureInfo.InvariantCulture);
                }

                var sample = new ThreadSample(user, system, voluntary, involuntary, waitNs);
                current[tid] = sample;
                if (previous is null)
                {
                    continue;
                }

                var name = GroupName(comm);
                if (!groups.TryGetValue(name, out var group))
                {
                    groups[name] = group = new Group();
                }

                var before = previous.TryGetValue(tid, out var known) ? known : default;
                group.Threads++;
                group.User += user - before.User;
                group.System += system - before.System;
                group.Voluntary += voluntary - before.Voluntary;
                group.Involuntary += involuntary - before.Involuntary;
                group.WaitNs += waitNs - before.WaitNs;
            }
            catch (Exception ex) when (ex is IOException or FormatException or IndexOutOfRangeException or UnauthorizedAccessException)
            {
                // The thread ended while it was being read.
            }
        }
    }

    private static void WriteThreads(int t, double seconds, Dictionary<string, Group> groups)
    {
        foreach (var (name, group) in groups.OrderByDescending(pair => pair.Value.User + pair.Value.System).Take(8))
        {
            string Cores(long jiffies) => (jiffies / Hz / seconds).ToString("0.00", CultureInfo.InvariantCulture);
            Console.WriteLine($"DIAG_THR t={t} group={name} threads={group.Threads} userCores={Cores(group.User)} sysCores={Cores(group.System)} vcs={group.Voluntary} ivcs={group.Involuntary} waitCores={(group.WaitNs / 1e9 / seconds).ToString("0.00", CultureInfo.InvariantCulture)}");
        }
    }

    private static void WriteRuntime(int t)
    {
        Console.WriteLine($"DIAG_RT t={t} tpThreads={ThreadPool.ThreadCount} tpPending={ThreadPool.PendingWorkItemCount} tpCompleted={ThreadPool.CompletedWorkItemCount} lockContention={Monitor.LockContentionCount} gc0={GC.CollectionCount(0)} gc1={GC.CollectionCount(1)} gc2={GC.CollectionCount(2)} allocBytes={GC.GetTotalAllocatedBytes(false)} gcPauseMs={GC.GetTotalPauseDuration().TotalMilliseconds.ToString("0", CultureInfo.InvariantCulture)}");
    }
}
