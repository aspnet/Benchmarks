using System.CommandLine;
using System.CommandLine.Invocation;
using System.CommandLine.Parsing;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Lighthouse;
using Lighthouse.Models;

var urlOption = new Option<string>(
    alias: "--url",
    description: "The URL to run Lighthouse against")
{
    IsRequired = true
};

var sampleCountOption = new Option<int>(
    alias: "--sample-count",
    getDefaultValue: static () => 3,
    description: "The number of times to run Lighthouse");
sampleCountOption.AddValidator(static result =>
{
    if (result.GetValueOrDefault<int>() is not > 0)
    {
        return "Must be an integer greater than zero";
    }

    return null;
});

var warmUpOption = new Option<bool>(
    alias: "--warm-up",
    description: "Whether to add a warm-up Lighthouse run");

var rootCommand = new RootCommand()
{
    urlOption,
    sampleCountOption,
    warmUpOption
};

var lighthouseReportJsonSerializerOptions = new JsonSerializerOptions()
{
    PropertyNameCaseInsensitive = true,
};

var benchmarkOutputJsonSerializerOptions = new JsonSerializerOptions()
{
    WriteIndented = true,
};

rootCommand.Handler = CommandHandler.Create(async (string url, int sampleCount, bool warmUp) =>
{
    Console.WriteLine("Generating Lighthouse report...");

    var benchmarkOutput = new BenchmarkOutput();
    var lighthouseArgs = url +
        " --chrome-flags=\"--no-sandbox --headless\"" +
        " --output=json" +
        " --only-audits=\"first-contentful-paint,interaction-to-next-paint,largest-contentful-paint,total-blocking-time\"";

    if (warmUp)
    {
        await RunLighthouseAsync(
            benchmarkOutput,
            lighthouseArgs,
            RunKind.WarmUp);
    }

    for (var i = 0; i < sampleCount; i++)
    {
        Console.WriteLine($"==== SAMPLE {i + 1} ====");

        await RunLighthouseAsync(
            benchmarkOutput,
            lighthouseArgs,
            i == 0 ? RunKind.FirstRun : RunKind.SuccessiveRun);
    }

    var benchmarkJson = JsonSerializer.Serialize(benchmarkOutput, benchmarkOutputJsonSerializerOptions);

    var outputBuilder = new StringBuilder();
    outputBuilder.AppendLine("#StartJobStatistics");
    outputBuilder.AppendLine(benchmarkJson);
    outputBuilder.AppendLine("#EndJobStatistics");

    Console.WriteLine(outputBuilder);
});

await rootCommand.InvokeAsync(args);

async Task RunLighthouseAsync(BenchmarkOutput benchmarkOutput, string args, RunKind runKind)
{
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

    if (runKind != RunKind.WarmUp)
    {
        var lighthouseReport = JsonSerializer.Deserialize<LighthouseReport>(lighthouseReportJson, lighthouseReportJsonSerializerOptions)
            ?? throw new InvalidOperationException("Could not parse Lighthouse report JSON");

        var addMetadata = runKind == RunKind.FirstRun;
        lighthouseReport.AddToBenchmarkOutput(benchmarkOutput, addMetadata);
        AddRawLighthouseJsonToBenchmarkOutput(lighthouseReportJson, benchmarkOutput, lighthouseReport.FetchTime, addMetadata);
    }

    static void AddRawLighthouseJsonToBenchmarkOutput(
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

enum RunKind
{
    WarmUp,
    FirstRun,
    SuccessiveRun
}
