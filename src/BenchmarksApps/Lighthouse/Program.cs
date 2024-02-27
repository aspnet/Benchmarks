using System.CommandLine;
using System.CommandLine.Invocation;
using System.CommandLine.Parsing;
using System.Text;
using System.Text.Json;
using Lighthouse;
using Lighthouse.Configuration;
using Lighthouse.Models;

var urlOption = new Option<string>(
    alias: "--url",
    description: "The URL to run Lighthouse against")
{
    IsRequired = true
};

var runCountOption = new Option<int>(
    alias: "--run-count",
    getDefaultValue: static () => 3,
    description: "The number of times to run Lighthouse");
runCountOption.AddValidator(static result =>
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
    runCountOption,
    warmUpOption
};

rootCommand.Handler = CommandHandler.Create(async (string url, int runCount, bool warmUp) =>
{
    Console.WriteLine("Generating Lighthouse report...");

    var benchmarkOutput = new BenchmarkOutput();
    var lighthouseArgs = url +
        " --chrome-flags=\"--no-sandbox --headless\"" +
        " --output=json" +
        " --only-audits=\"first-contentful-paint,interaction-to-next-paint,largest-contentful-paint,total-blocking-time\"";

    if (warmUp)
    {
        Console.WriteLine($"==== WARM-UP ====");

        await LighthouseRunner.RunAsync(
            benchmarkOutput,
            lighthouseArgs,
            LighthouseRunKind.WarmUp);
    }

    for (var i = 0; i < runCount; i++)
    {
        Console.WriteLine($"==== RUN {i + 1} ====");

        await LighthouseRunner.RunAsync(
            benchmarkOutput,
            lighthouseArgs,
            i == 0 ? LighthouseRunKind.FirstRun : LighthouseRunKind.SuccessiveRun);
    }

    var benchmarkJson = JsonSerializer.Serialize(benchmarkOutput, JsonOptions.BenchmarkOutputOptions);

    var outputBuilder = new StringBuilder();
    outputBuilder.AppendLine("#StartJobStatistics");
    outputBuilder.AppendLine(benchmarkJson);
    outputBuilder.AppendLine("#EndJobStatistics");

    Console.WriteLine(outputBuilder);
});

await rootCommand.InvokeAsync(args);
