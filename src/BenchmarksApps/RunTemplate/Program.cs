using System.CommandLine;
using System.CommandLine.Parsing;
using RunTemplate;
using RunTemplate.Scenarios;

var rootCommand = new RootCommand
{
    GlobalOptions.UrlsOption,

    new Command("blazor", "Create/run a Blazor Web App")
    {
        new Option<string>(
            ["--interactivity", "-int"],
            getDefaultValue: () => "None",
            description: "Interactivity type")
            .FromAmong("Server", "WebAssembly", "Auto", "None"),
    }
        .WithTemplateScenarioHandler<BlazorWebScenario>(),

    new Command("blazorwasm", "Create/run a Blazor WebAssembly Standalone App")
        .WithTemplateScenarioHandler<BlazorWebAssemblyStandaloneScenario>(),
};

return await rootCommand.InvokeAsync(args);
