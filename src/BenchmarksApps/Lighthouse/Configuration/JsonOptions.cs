using System.Text.Json;

namespace Lighthouse.Configuration;

internal static class JsonOptions
{
    public static readonly JsonSerializerOptions LighthouseReportOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static readonly JsonSerializerOptions BenchmarkOutputOptions = new()
    {
        WriteIndented = true,
    };
}
