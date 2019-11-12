// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using McMaster.Extensions.CommandLineUtils;

namespace JobConsumer
{
    public class Program
    {
        private static readonly TimeSpan DriverTimeout = TimeSpan.FromMinutes(10);

        private static string JobsPath { get; set; }
        private static string RepoPath { get; set; }
        private static string DriverPath { get; set; }
        private static string ServerUrl { get; set; }
        private static string ClientUrl { get; set; }

        private static string ProcessingPath => Path.Combine(JobsPath, "processing");
        private static string ProcessedPath => Path.Combine(JobsPath, "processed");

        public static int Main(string[] args)
        {
            var app = new CommandLineApplication();

            app.HelpOption("-h|--help");
            var jobsPath = app.Option("-j|--jobs-path <PATH>", "The path where jobs are created", CommandOptionType.SingleValue).IsRequired();
            var repoPath = app.Option("-r|--repo-path <PATH>", "The path to the repo being benchmarked", CommandOptionType.SingleValue).IsRequired();
            var driverPath = app.Option("-d|--driver <PATH>", "The BenchmarksDriver assembly file path", CommandOptionType.SingleValue).IsRequired();
            var serverUrl = app.Option("-s|--server <URL>", "The server url", CommandOptionType.SingleValue).IsRequired();
            var clientUrl = app.Option("-c|--client <URL>", "The client url", CommandOptionType.SingleValue).IsRequired();

            app.OnExecuteAsync(async cancellationToken =>
            {
                JobsPath = jobsPath.Value();
                RepoPath = repoPath.Value();
                DriverPath = driverPath.Value();
                ServerUrl = serverUrl.Value();
                ClientUrl = clientUrl.Value();

                var directory = new DirectoryInfo(JobsPath);

                if (!directory.Exists)
                {
                    Console.WriteLine($"The path doesn't exist: '{directory.FullName}'");
                    return -1;
                }

                if (!File.Exists(DriverPath))
                {
                    Console.WriteLine($"The driver could not be found at: '{DriverPath}'");
                    return -1;
                }

                // Create the target folders if they don't exist
                Directory.CreateDirectory(ProcessingPath);
                Directory.CreateDirectory(ProcessedPath);

                Console.WriteLine("Press enter to exit.");

                while (true)
                {
                    // Get oldest file
                    var nextFile = directory
                        .GetFiles()
                        .OrderByDescending(f => f.LastWriteTime)
                        .FirstOrDefault();


                    // If no file was found, wait some time
                    if (nextFile is null)
                    {
                        if (Console.KeyAvailable)
                        {
                            if (Console.ReadKey().Key == ConsoleKey.Enter)
                            {
                                return 0;
                            }
                        }

                        await Task.Delay(1000);
                        continue;
                    }

                    Console.WriteLine($"Found '{nextFile.Name}'");

                    // Attempting to move the file to the processing folder in order to lock it
                    var session = nextFile.Name.Split('.', 2)[0];
                    var processingFilePath = Path.Combine(ProcessingPath, nextFile.Name);
                    var processingFile = new FileInfo(processingFilePath);

                    // If we can't move the file to the processing folder, we continue, which might retry the same file
                    try
                    {
                        nextFile.MoveTo(processingFilePath);
                    }
                    catch
                    {
                        Console.WriteLine($"The file named '{nextFile.FullName}' couldn't be moved to '{processingFilePath}'. Skipping...");
                        continue;
                    }

                    var benchmarkResult = await BenchmarkPR(processingFile, session);
                    await PublishResult(processingFile, benchmarkResult);
                }
            });

            return app.Execute(args);
        }

        private static async Task<BenchmarkResult> BenchmarkPR(FileInfo processingFile, string session)
        {
            var currentWorkDir = Directory.GetCurrentDirectory();
            Directory.SetCurrentDirectory(RepoPath);

            try
            {
                var outputBuilder = new StringBuilder();
                var errorBuilder = new StringBuilder();

                var buildRules = await GetBuildInstructions(processingFile);

                Process.Start("git", "clean -xdf").WaitForExit();
                Process.Start("git", "fetch").WaitForExit();
                Process.Start("git", $"checkout {buildRules.BaselineSHA}").WaitForExit();

                RunBuildCommands(buildRules);

                var baselineArguments = $"{DriverPath} --server {ServerUrl} --client {ClientUrl} --jobs {processingFile.FullName} --session {session} --self-contained --save baseline --description Before";

                outputBuilder.AppendLine($"Starting baseline run on '{buildRules.BaselineSHA}'...");
                var baselineSuccess = await RunDriver(baselineArguments, outputBuilder, errorBuilder);

                if (!baselineSuccess)
                {
                    errorBuilder.AppendLine($"Baseline benchmark run on '{buildRules.BaselineSHA}' failed.");
                    return new BenchmarkResult
                    {
                        Success = false,
                        BaselineStdout = outputBuilder.ToString(),
                        BaselineStderr = errorBuilder.ToString(),
                    };
                }

                var baselineStdout = outputBuilder.ToString();
                var baselinseStderr = errorBuilder.ToString();
                outputBuilder.Clear();
                errorBuilder.Clear();

                Process.Start("git", $"checkout {buildRules.PullRequestSHA}").WaitForExit();

                RunBuildCommands(buildRules);

                var prArguments = $"{DriverPath} --server {ServerUrl} --client {ClientUrl} --jobs {processingFile.FullName} --session {session} --self-contained --diff baseline --description After";

                outputBuilder.AppendLine($"Starting PR run on '{buildRules.PullRequestSHA}'...");
                var prSuccess = await RunDriver(prArguments, outputBuilder, errorBuilder);

                if (!prSuccess)
                {
                    errorBuilder.AppendLine($"PR benchmark run on '{buildRules.PullRequestSHA}' failed.");
                }

                return new BenchmarkResult
                {
                    Success = prSuccess,
                    BaselineStdout = baselineStdout,
                    BaselineStderr = baselinseStderr,
                    PullRequestStdout = outputBuilder.ToString(),
                    PullRequestStderr = errorBuilder.ToString(),
                };
            }
            finally
            {
                Directory.SetCurrentDirectory(currentWorkDir);
            }
        }

        private static async Task<bool> RunDriver(string arguments, StringBuilder outputBuilder, StringBuilder errorBuilder)
        {
            outputBuilder.AppendLine("Current dir: " + Directory.GetCurrentDirectory());
            outputBuilder.AppendLine("Args: " + arguments);

            using var process = new Process()
            {
                StartInfo =
                {
                    FileName = GetDotNetExecutable(),
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                },
            };

            var sawErrorOutput = false;

            process.OutputDataReceived += (_, e) =>
            {
                outputBuilder.AppendLine(e.Data);
                Console.WriteLine(e.Data);
            };

            process.ErrorDataReceived += (_, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    sawErrorOutput = true;
                }

                // Don't omit all newlines, but if there has been nothing but
                // whitespace so far, ignore the error output.
                if (sawErrorOutput)
                {
                    errorBuilder.AppendLine(e.Data);
                    Console.Error.WriteLine(e.Data);
                }
            };

            process.Start();

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            var start = Environment.TickCount64;

            while (true)
            {
                if (process.HasExited)
                {
                    break;
                }

                if (Environment.TickCount64 - start > DriverTimeout.TotalMilliseconds)
                {
                    Console.WriteLine("Driver timed out, skipping job");
                    errorBuilder.AppendLine("Driver timed out, skipping job");
                    process.Kill();

                    return false;
                }

                await Task.Delay(1000);
            }

            // Job succeeded?
            return process.ExitCode == 0;
        }

        private static async Task<BuildInstructions> GetBuildInstructions(FileInfo processingFile)
        {
            using var processingJsonStream = File.OpenRead(processingFile.FullName);
            using var jsonDocument = await JsonDocument.ParseAsync(processingJsonStream);

            foreach (var element in jsonDocument.RootElement.EnumerateObject())
            {
                if (element.NameEquals(nameof(BuildInstructions)))
                {
                    return JsonSerializer.Deserialize<BuildInstructions>(element.Value.GetRawText());
                }
            }

            throw new InvalidDataException($"Job file {processingFile.Name} doesn't include a top-level '{nameof(BuildInstructions)}' property.");
        }

        private static void RunBuildCommands(BuildInstructions buildRules)
        {
            foreach (var buildCommand in buildRules.BuildCommands)
            {
                var splitCommand = buildCommand.Split(' ', 2);
                Process.Start(splitCommand[0], splitCommand.Length == 2 ? splitCommand[1] : string.Empty).WaitForExit();
            }
        }

        private static async Task PublishResult(FileInfo processingFile, BenchmarkResult jobResult)
        {
            using (var processingJsonStream = File.Open(processingFile.FullName, FileMode.Open))
            {
                var jsonDictionary = await JsonSerializer.DeserializeAsync<Dictionary<string, object>>(processingJsonStream);

                jsonDictionary[nameof(BenchmarkResult)] = jobResult;

                // Clear file and reset position to 0
                processingJsonStream.SetLength(0);
                await JsonSerializer.SerializeAsync(processingJsonStream, jsonDictionary, new JsonSerializerOptions
                {
                    WriteIndented = true,
                });
            }

            processingFile.MoveTo(Path.Combine(ProcessedPath, processingFile.Name));
        }

        private static string GetDotNetExecutable()
        {
            return RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? "dotnet.exe"
                : "dotnet"
                ;
        }

        // REVIEW: What's the best way to share these DTOs in this repo?
        private class BuildInstructions
        {
            public string[] BuildCommands { get; set; }

            public string BaselineSHA { get; set; }
            public string PullRequestSHA { get; set; }
        }

        private class BenchmarkResult
        {
            public bool Success { get; set; }
            public string BaselineStdout { get; set; }
            public string BaselineStderr { get; set; }
            public string PullRequestStdout { get; set; }
            public string PullRequestStderr { get; set; }
        }
    }
}
