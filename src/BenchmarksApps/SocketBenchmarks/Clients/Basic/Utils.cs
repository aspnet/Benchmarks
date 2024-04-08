using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SocketBenchmarks.Clients.Basic
{
    public static class Utils
    {
        private static readonly double s_msPerTick = 1000.0 / Stopwatch.Frequency;

        public static void Log(string message)
        {
            var time = DateTime.UtcNow.ToString("hh:mm:ss.fff");
            Console.WriteLine($"[{time}] {message}");
        }

        public static double TicksToMs(double ticks) => ticks * s_msPerTick;

        public static double GetPercentile(int percent, List<long> sortedValues)
        {
            if (percent == 0)
            {
                return sortedValues[0];
            }

            if (percent == 100)
            {
                return sortedValues[^1];
            }

            var i = ((long)percent * sortedValues.Count) / 100.0 + 0.5;
            var fractionPart = i - Math.Truncate(i);

            return (1.0 - fractionPart) * sortedValues[(int)Math.Truncate(i) - 1] + fractionPart * sortedValues[(int)Math.Ceiling(i) - 1];
        }

        public static void RegisterPercentiledMetric(string name, string shortDescription, string longDescription)
        {
            BenchmarksEventSource.Register(name + "/min", Operations.Min, Operations.Min, shortDescription + " - min", longDescription + " - min", "n2");
            BenchmarksEventSource.Register(name + "/p50", Operations.Max, Operations.Max, shortDescription + " - p50", longDescription + " - 50th percentile", "n2");
            BenchmarksEventSource.Register(name + "/p75", Operations.Max, Operations.Max, shortDescription + " - p75", longDescription + " - 75th percentile", "n2");
            BenchmarksEventSource.Register(name + "/p90", Operations.Max, Operations.Max, shortDescription + " - p90", longDescription + " - 90th percentile", "n2");
            BenchmarksEventSource.Register(name + "/p99", Operations.Max, Operations.Max, shortDescription + " - p99", longDescription + " - 99th percentile", "n2");
            BenchmarksEventSource.Register(name + "/max", Operations.Max, Operations.Max, shortDescription + " - max", longDescription + " - max", "n2");
        }

        public static void LogPercentiledMetric(string name, List<long> values, Func<double, double> prepareValue)
        {
            values.Sort();

            LogMetric(name + "/min", prepareValue(GetPercentile(0, values)));
            LogMetric(name + "/p50", prepareValue(GetPercentile(50, values)));
            LogMetric(name + "/p75", prepareValue(GetPercentile(75, values)));
            LogMetric(name + "/p90", prepareValue(GetPercentile(90, values)));
            LogMetric(name + "/p99", prepareValue(GetPercentile(99, values)));
            LogMetric(name + "/max", prepareValue(GetPercentile(100, values)));
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

        public static double GetPercentile(int percent, List<double> sortedValues)
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

        public static void LogMetric(string name, double value)
        {
            BenchmarksEventSource.Measure(name, value);
            Log($"{name}: {value}");
        }

        public static void LogMetric(string name, long value)
        {
            BenchmarksEventSource.Measure(name, value);
            Log($"{name}: {value}");
        }
    }
}
