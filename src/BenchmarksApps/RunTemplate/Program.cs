using System.CommandLine;
using System.CommandLine.Invocation;
using System.Diagnostics;
using Microsoft.Crank.EventSources;

var templateArgsOption = new Option<string>(
    alias: "--template-args",
    description: "The arguments to be passed to 'dotnet new' (without specifying a name or output path)")
{
    IsRequired = true,
};

var projectNameOption = new Option<string>(
    alias: "--project-name",
    getDefaultValue: static () => "DotNetApp",
    description: "The name to use for the project created from the template");

var mainProjectRelativePathOption = new Option<string?>(
    alias: "--main-project-relative-path",
    description: "The path of the project to publish, relative to the root of the generated project");

var runArgsOption = new Option<string?>(
    alias: "--run-args",
    description: "The arguments to be passed when executing the template");

var rootCommand = new RootCommand
{
    templateArgsOption,
    projectNameOption,
    mainProjectRelativePathOption,
    runArgsOption,
};

rootCommand.Handler = CommandHandler.Create(async (InvocationContext context) =>
{
    var templateArgs = context.ParseResult.ValueForOption(templateArgsOption);
    var projectName = context.ParseResult.ValueForOption(projectNameOption);
    var mainProjectRelativePath = context.ParseResult.ValueForOption(mainProjectRelativePathOption);
    var templateRunArgs = context.ParseResult.ValueForOption(runArgsOption);

    var dotnetPath = Environment.GetEnvironmentVariable("DOTNET_EXE") ?? "dotnet";

    Console.WriteLine($"dotnet path: {dotnetPath}");

    // Run "dotnet --info" for debugging purposes
    await RunDotNetCommand(dotnetPath, "--info");

    var workingDirectory = Path.Combine(Directory.GetCurrentDirectory(), Path.GetRandomFileName());
    Directory.CreateDirectory(workingDirectory);

    await RunDotNetCommand(dotnetPath, $"new {templateArgs} -n {projectName} -o {workingDirectory}");

    var publishDirectory = Path.Combine(workingDirectory, "publish");
    var mainProjectAbsolutePath = string.IsNullOrWhiteSpace(mainProjectRelativePath)
        ? workingDirectory
        : Path.Combine(workingDirectory, mainProjectRelativePath);

    await RunDotNetCommand(dotnetPath, $"publish -c Release -o {publishDirectory}", mainProjectAbsolutePath);

    var templateFileName = Path.Combine(publishDirectory, $"{projectName}.dll");
    var templateProcessArgs = string.Join(" ", (string?[]) [templateFileName, templateRunArgs]);

    await RunDotNetCommand(dotnetPath, templateProcessArgs, workingDirectory: publishDirectory);

    static async Task RunDotNetCommand(string dotnetPath, string args, string workingDirectory = "")
    {
        Console.WriteLine($"Executing '{dotnetPath} {args}'...");

        var processStartInfo = new ProcessStartInfo
        {
            FileName = dotnetPath,
            Arguments = args,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            Environment =
            {
                ["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1",
                ["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1",
            },
        };

        var process = System.Diagnostics.Process.Start(processStartInfo)
            ?? throw new InvalidOperationException($"Could not start dotnet process");

        // In case the parent process gets killed, we want Crank to kill this child process
        // as well.
        BenchmarksEventSource.SetChildProcessId(process.Id);

        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"dotnet process exited with code {process.ExitCode}");
        }
    }
});

await rootCommand.InvokeAsync(args);
