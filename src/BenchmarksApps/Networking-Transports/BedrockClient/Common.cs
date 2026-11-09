using System.Net.Transport;

internal static class Backends
{
    public static NetworkEngineOptions Create(string name, int workers) => name switch
    {
        "iocp" => new IocpEngineOptions { WorkerCount = workers },
        "rio" => new RioEngineOptions { WorkerCount = workers },
        "epoll" => new EpollEngineOptions { WorkerCount = workers },
        "iouring" => new IoUringEngineOptions { WorkerCount = workers },
        _ => throw new ArgumentException($"Unknown handler '{name}'. Use sockets, iocp, rio, epoll or iouring.")
    };
}

internal sealed class Arguments(Dictionary<string, string> values)
{
    public static Arguments Parse(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i].StartsWith("--", StringComparison.Ordinal))
            {
                values[args[i][2..]] = i + 1 < args.Length ? args[++i] : "true";
            }
        }
        return new Arguments(values);
    }

    public string Get(string key, string fallback) => values.TryGetValue(key, out var value) ? value : fallback;

    public int Get(string key, int fallback) => values.TryGetValue(key, out var value) ? int.Parse(value, System.Globalization.CultureInfo.InvariantCulture) : fallback;
}
