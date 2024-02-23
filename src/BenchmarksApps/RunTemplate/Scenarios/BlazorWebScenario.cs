using RunTemplate.Helpers;

namespace RunTemplate.Scenarios;

internal sealed class BlazorWebScenario : ITemplateScenario
{
    private const string ProjectName = "BlazorApp";
    private const string PublishFolderName = "publish";

    private readonly string _workingDirectory;
    private readonly DotNet _dotnet;

    public required string Interactivity { get; init; }

    public BlazorWebScenario()
    {
        _workingDirectory = Path.Combine(Directory.GetCurrentDirectory(), Path.GetRandomFileName());
        _dotnet = DotNet.Create(_workingDirectory, verbose: false);
    }

    public async Task BuildAsync()
    {
        Directory.CreateDirectory(_workingDirectory);

        await _dotnet.ExecuteAsync($"new blazor -int {Interactivity} -n {ProjectName} -o .");

        await _dotnet.ExecuteAsync($"publish -o ./{PublishFolderName}");
    }

    public async Task RunAsync(string urls, Action notifyReady)
    {
        var publishDirectory = Path.Combine(_workingDirectory, PublishFolderName);
        var relativeFileName = $"{ProjectName}.dll";

        using var aspNetProcess = AspNetProcess.Start(_dotnet, publishDirectory, relativeFileName, urls);

        await aspNetProcess.WaitForApplicationStartAsync();

        notifyReady();

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var exitCode = await aspNetProcess.WaitForExitAsync(cts.Token);

        Console.WriteLine($"Application exited with exit code {exitCode}.");
    }
}
