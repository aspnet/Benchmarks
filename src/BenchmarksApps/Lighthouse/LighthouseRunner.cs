using System.Diagnostics;
using System.Text.Json;
using Lighthouse.Configuration;
using Lighthouse.Models;

namespace Lighthouse;

internal static class LighthouseRunner
{
    public static async Task RunAsync(BenchmarkOutput benchmarkOutput, string args, LighthouseRunKind runKind)
    {
        var processStartInfo = new ProcessStartInfo()
        {
            FileName = "lighthouse",
            Arguments = args,
            UseShellExecute = false,
            RedirectStandardOutput = true,
        };

        using var process = Process.Start(processStartInfo)
            ?? throw new InvalidOperationException("Unable to start Lighthouse");

        var lighthouseReportJson = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"Lighthouse failed with exit code {process.ExitCode}");
        }

        if (runKind != LighthouseRunKind.WarmUp)
        {
            var lighthouseReport = JsonSerializer.Deserialize<LighthouseReport>(lighthouseReportJson, JsonOptions.LighthouseReportOptions)
                ?? throw new InvalidOperationException("Could not parse Lighthouse report JSON");

            var addMetadata = runKind == LighthouseRunKind.FirstRun;
            lighthouseReport.AddToBenchmarkOutput(benchmarkOutput, addMetadata);
            AddRawLighthouseJsonToBenchmarkOutput(lighthouseReportJson, benchmarkOutput, lighthouseReport.FetchTime, addMetadata);
        }
    }

    private static void AddRawLighthouseJsonToBenchmarkOutput(
        string lighthouseReportJson,
        BenchmarkOutput benchmarkOutput,
        DateTime timestamp,
        bool addMetadata)
    {
        if (addMetadata)
        {
            benchmarkOutput.Metadata.Add(new()
            {
                Source = BenchmarkOutputSettings.Source,
                Name = "lighthouse/raw",
                Aggregate = Operation.All,
                Reduce = Operation.All,
                LongDescription = "Raw output",
                ShortDescription = "Raw output",
                Format = "json",
            });
        }

        benchmarkOutput.Measurements.Add(new()
        {
            Name = "lighthouse/raw",
            Timestamp = timestamp,
            Value = lighthouseReportJson,
        });
    }
}
