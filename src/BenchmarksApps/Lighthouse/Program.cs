using System.CommandLine;
using System.CommandLine.Invocation;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Lighthouse;
using Lighthouse.Models;

var rootCommand = new RootCommand()
{
    new Option<string>("--url", "The URL to run Lighthouse against")
    {
        IsRequired = true
    },
};

var lighthouseReportJsonSerializerOptions = new JsonSerializerOptions()
{
    PropertyNameCaseInsensitive = true,
};

var benchmarkOutputJsonSerializerOptions = new JsonSerializerOptions()
{
    WriteIndented = true,
};

rootCommand.Handler = CommandHandler.Create(async (string url) =>
{
    Console.WriteLine("Generating Lighthouse report...");

    var args = url +
        " --chrome-flags=\"--no-sandbox --headless\"" +
        " --output=json" +
        " --only-audits=\"first-contentful-paint,interaction-to-next-paint,largest-contentful-paint,total-blocking-time\"";

    var processStartInfo = new ProcessStartInfo()
    {
        FileName = "lighthouse",
        Arguments = args,
        UseShellExecute = false,
        RedirectStandardOutput = true,
    };

    using var process = System.Diagnostics.Process.Start(processStartInfo)
        ?? throw new InvalidOperationException("Unable to start Lighthouse");

    var lighthouseReportJson = await process.StandardOutput.ReadToEndAsync();
    await process.WaitForExitAsync();

    if (process.ExitCode != 0)
    {
        throw new InvalidOperationException($"Lighthouse failed with exit code {process.ExitCode}");
    }

    Console.WriteLine("==== REPORT RESULTS ====");
    Console.WriteLine(lighthouseReportJson);

    var benchmarkOutput = new BenchmarkOutput();
    var lighthouseReport = JsonSerializer.Deserialize<LighthouseReport>(lighthouseReportJson, lighthouseReportJsonSerializerOptions)
        ?? throw new InvalidOperationException("Could not parse Lighthouse report JSON");

    lighthouseReport.AddToBenchmarkOutput(benchmarkOutput);
    AddRawLighthouseJsonToBenchmarkOutput(lighthouseReportJson, benchmarkOutput, lighthouseReport.FetchTime);

    var benchmarkJson = JsonSerializer.Serialize(benchmarkOutput, benchmarkOutputJsonSerializerOptions);

    var outputBuilder = new StringBuilder();
    outputBuilder.AppendLine("#StartJobStatistics");
    outputBuilder.AppendLine(benchmarkJson);
    outputBuilder.AppendLine("#EndJobStatistics");

    Console.WriteLine(outputBuilder);

    static void AddRawLighthouseJsonToBenchmarkOutput(string lighthouseReportJson, BenchmarkOutput benchmarkOutput, DateTime timestamp)
    {
        benchmarkOutput.Metadata.Add(new()
        {
            Source = BenchmarkOutputSettings.Source,
            Name = "lighthouse/raw",
            LongDescription = "Raw output",
            ShortDescription = "Raw output",
            Format = "json",
        });

        benchmarkOutput.Measurements.Add(new()
        {
            Name = "lighthouse/raw",
            Timestamp = timestamp,
            Value = lighthouseReportJson,
        });
    }
});

await rootCommand.InvokeAsync(args);
