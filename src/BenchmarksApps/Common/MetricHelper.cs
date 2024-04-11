using Microsoft.Crank.EventSources;

namespace Common;

internal static class Logger
{
    public static class MetricName
    {
        public const string Avg = "/avg";
        public const string Min = "/min";
        public const string Max = "/max";
        public const string P50 = "/p50";
        public const string P75 = "/p75";
        public const string P90 = "/p90";
        public const string P99 = "/p99";
    }

    private static readonly Dictionary<string, string> s_metricFormats = [];

    public static void RegisterSimpleMetric(string name, string description, string format = "n2")
        => RegisterSimpleMetric(name, description, description, format);

    public static void RegisterSimpleMetric(string name, string shortDescription, string longDescription, string format)
    {
        BenchmarksEventSource.Register(name, Operations.First, Operations.First, shortDescription, longDescription, format);
        s_metricFormats[name] = format;
    }

    public static void RegisterPercentiledMetric(string name, string shortDescription, string longDescription, string format = "n3")
    {
        RegisterSimpleMetric(name + MetricName.Avg, shortDescription + " - avg", longDescription + " - avg", format);
        RegisterSimpleMetric(name + MetricName.Min, shortDescription + " - min", longDescription + " - min", format);
        RegisterSimpleMetric(name + MetricName.P50, shortDescription + " - p50", longDescription + " - 50th percentile", format);
        RegisterSimpleMetric(name + MetricName.P75, shortDescription + " - p75", longDescription + " - 75th percentile", format);
        RegisterSimpleMetric(name + MetricName.P90, shortDescription + " - p90", longDescription + " - 90th percentile", format);
        RegisterSimpleMetric(name + MetricName.P99, shortDescription + " - p99", longDescription + " - 99th percentile", format);
        RegisterSimpleMetric(name + MetricName.Max, shortDescription + " - max", longDescription + " - max", format);
    }

    public static void Log(string message)
    {
        var time = DateTime.UtcNow.ToString("hh:mm:ss.fff");
        Console.WriteLine($"[{time}] {message}");
    }

    public static void LogMetric(string name, double value)
    {
        if (!s_metricFormats.TryGetValue(name, out var format))
        {
            throw new InvalidOperationException($"Metric '{name}' is not registered.");
        }
        BenchmarksEventSource.Measure(name, value);
        Log($"  {name}: {value.ToString(format)}");
    }

    public static void LogPercentiledMetric(string name, List<double> values)
    {
        values.Sort();

        LogMetric(name + "/avg", values.Average());
        LogMetric(name + "/min", GetPercentile(0, values));
        LogMetric(name + "/p50", GetPercentile(50, values));
        LogMetric(name + "/p75", GetPercentile(75, values));
        LogMetric(name + "/p90", GetPercentile(90, values));
        LogMetric(name + "/p99", GetPercentile(99, values));
        LogMetric(name + "/max", GetPercentile(100, values));
    }

    private static double GetPercentile(int percent, List<double> sortedValues)
    {
        if (percent == 0)
        {
            return sortedValues[0];
        }

        if (percent == 100)
        {
            return sortedValues[^1];
        }

        var i = percent * sortedValues.Count / 100.0 + 0.5;
        var fractionPart = i - Math.Truncate(i);

        return (1.0 - fractionPart) * sortedValues[(int)Math.Truncate(i) - 1] + fractionPart * sortedValues[(int)Math.Ceiling(i) - 1];
    }
}
