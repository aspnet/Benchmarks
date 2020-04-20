// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.IO;
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

        private static string ServiceBus { get; set; }
        private static string Queue { get; set; }
        private static string DriverPath { get; set; }
        private static string DriverArgs { get; set; }
        private static string ClientUrl { get; set; }

        public static int Main(string[] args)
        {
            var app = new CommandLineApplication();

            app.HelpOption("-h|--help");
            var serviceBusOption = app.Option("-sb|--service-bus <string>", "The Azure Service Bus connection string", CommandOptionType.SingleValue).IsRequired();
            var queueOption = app.Option("-q|--queue <string>", "The service bus queue", CommandOptionType.SingleValue).IsRequired();
            var driverPathOption = app.Option("-d|--driver <PATH>", "The BenchmarksDriver assembly file path", CommandOptionType.SingleValue).IsRequired();
            var driverArgsOption = app.Option("-a|--args <args>", "The extra driver arguments", CommandOptionType.SingleValue);

            app.OnExecuteAsync(async cancellationToken =>
            {
                ServiceBus = serviceBusOption.Value();
                Queue = queueOption.Value();
                DriverPath = driverPathOption.Value();
                DriverArgs = driverArgsOption.HasValue() ? driverArgsOption.Value() : "";


                if (!File.Exists(DriverPath))
                {
                    Console.WriteLine($"The driver could not be found at: '{DriverPath}'");
                    return -1;
                }

                Console.WriteLine("Press enter to exit.");

                while (true)
                {
                    FileInfo nextFile = null;

                    // Get oldest file
                    try
                    {
                        // Get next message in queue
                    }
                    catch (Exception ex)
                    {
                    }

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

                }
            });

            return app.Execute(args);
        }

        private static async Task<bool> RunDriver(string arguments, StringBuilder outputBuilder, StringBuilder errorBuilder)
        {
            Console.WriteLine($"Running driver with arguments: {arguments}");

            // Don't let the repo's global.json interfere with running the driver
            File.Move("global.json", "global.json~");

            try
            {
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
                    if (!string.IsNullOrWhiteSpace(e.Data))
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
            finally
            {
                File.Move("global.json~", "global.json");
            }
        }

        private static string GetDotNetExecutable()
        {
            return RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? "dotnet.exe"
                : "dotnet"
                ;
        }

        private static void RunCommand(string command)
        {
            Console.WriteLine($"Running command: '{command}'");

            var outputBuilder = new StringBuilder();

            var splitCommand = command.Split(' ', 2);
            var fileName = splitCommand[0];
            var arguments = splitCommand.Length == 2 ? splitCommand[1] : string.Empty;

            using var process = new Process()
            {
                StartInfo =
                {
                    FileName = fileName,
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                },
            };

            process.OutputDataReceived += (_, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data))
                {
                    outputBuilder.AppendLine($"stdout: {e.Data}");
                    Console.WriteLine(e.Data);
                }
            };

            process.ErrorDataReceived += (_, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data))
                {
                    outputBuilder.AppendLine($"stderr: {e.Data}");
                    Console.Error.WriteLine(e.Data);
                }
            };

            process.Start();

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            process.WaitForExit();

            if (process.ExitCode != 0)
            {
                throw new Exception($"Process '{fileName} {arguments}' exited with exit code '{process.ExitCode}' and the following output:\n\n{outputBuilder}");
            }
        }

        private static async Task<bool> WaitForCompleteJsonFile(FileInfo nextFile)
        {
            // Wait up to 5 seconds for the Json file to be fully parsable.
            for (int i = 0; i < 5; i++)
            {
                try
                {
                    using var processedJsonStream = File.OpenRead(nextFile.FullName);
                    using var jsonDocument = await JsonDocument.ParseAsync(processedJsonStream);

                    return true;
                }
                catch (JsonException)
                {
                    if (i == 4)
                    {
                        throw;
                    }

                    await Task.Delay(1000);
                }
            }

            return false;
        }

        private static async Task<string> GetSdkVersionOrNull()
        {
            if (!File.Exists("global.json"))
            {
                return null;
            }

            using var globalJsonStream = File.OpenRead("global.json");
            using var jsonDocument = await JsonDocument.ParseAsync(globalJsonStream);

            if (jsonDocument.RootElement.TryGetProperty("sdk", out var sdkElement) && sdkElement.ValueKind == JsonValueKind.Object)
            {
                if (sdkElement.TryGetProperty("version", out var sdkVersionElement) && sdkVersionElement.ValueKind == JsonValueKind.String)
                {
                    return sdkVersionElement.GetString();
                }
            }

            return null;
        }
    }
}
