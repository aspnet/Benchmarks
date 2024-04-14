// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.Crank.EventSources;

namespace System.Net.Benchmarks;

public static class MetricName
{
    public const string Avg = "/avg";
    public const string Min = "/min";
    public const string P50 = "/p50";
    public const string P75 = "/p75";
    public const string P90 = "/p90";
    public const string P99 = "/p99";
    public const string Max = "/max";
    public const string Mean = "/mean";
    public const string Read = "/read";
    public const string Write = "/write";
    public const string Rps = "/rps";
    public const string Errors = "/errors";
    public const string Handshake = "/handshake";
}

public static class LogHelper
{
    private static readonly Dictionary<string, (string Description, string Format, bool IsLogged)> s_metrics = [];

    public static void RegisterSimpleMetric(string name, string description, string format)
        => RegisterSimpleMetric(name, description, description, format);

    public static void RegisterSimpleMetric(string name, string shortDescription, string longDescription, string format)
    {
        if (s_metrics.ContainsKey(name))
        {
            throw new InvalidOperationException($"Metric '{name}' is already registered.");
        }
        s_metrics[name] = (longDescription, format, IsLogged: false);
        BenchmarksEventSource.Register(name, Operations.First, Operations.First, shortDescription, longDescription, format);
    }

    public static void RegisterPercentileMetric(string name, string shortDescription, string longDescription, string format)
    {
        RegisterSimpleMetric(name + MetricName.Avg, shortDescription + " - mean", longDescription + " - mean", format);
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
        if (!s_metrics.TryGetValue(name, out var metric))
        {
            throw new InvalidOperationException($"Metric '{name}' is not registered.");
        }

        if (metric.IsLogged)
        {
            throw new InvalidOperationException($"Metric '{name}' is already logged.");
        }
        s_metrics[name] = metric with { IsLogged = true };

        BenchmarksEventSource.Measure(name, value);
        Log($"    {name} -- {metric.Description}");
        Log($"    {value.ToString(metric.Format)}");
    }

    public static void LogPercentileMetric(string name, List<double> values)
    {
        values.Sort();

        LogMetric(name + MetricName.Avg, values.Average());
        LogMetric(name + MetricName.Min, GetPercentile(0, values));
        LogMetric(name + MetricName.P50, GetPercentile(50, values));
        LogMetric(name + MetricName.P75, GetPercentile(75, values));
        LogMetric(name + MetricName.P90, GetPercentile(90, values));
        LogMetric(name + MetricName.P99, GetPercentile(99, values));
        LogMetric(name + MetricName.Max, GetPercentile(100, values));
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
