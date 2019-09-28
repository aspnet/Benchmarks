using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using McMaster.Extensions.CommandLineUtils;

namespace JobConsumer
{
    class Program
    {
        static readonly TimeSpan DriverTimeout = TimeSpan.FromMinutes(20);
        
        public static int Main(string[] args)
        {
            var app = new CommandLineApplication();

            app.HelpOption("-h|--help");
            var optionPath = app.Option("-p|--path <PATH>", "The path where jobs are created", CommandOptionType.SingleValue).IsRequired();
            var optionServer = app.Option("-s|--server <URL>", "The server url", CommandOptionType.SingleValue).IsRequired();
            var optionClient = app.Option("-c|--client <URL>", "The client url", CommandOptionType.SingleValue).IsRequired();
            var optionDriver = app.Option("-d|--driver <PATH>", "The BenchmarksDriver assembly file path", CommandOptionType.SingleValue).IsRequired();

            app.OnExecuteAsync(async cancellationToken =>
            {
                var directory = new DirectoryInfo(optionPath.Value());

                if (!directory.Exists)
                {
                    Console.WriteLine($"The path doesn't exist: '{directory.FullName}'");
                    return -1;
                }

                if (!File.Exists(optionDriver.Value()))
                {
                    Console.WriteLine($"The driver could not be found at: '{optionDriver.Value()}'");
                    return -1;
                }

                // Create the target folders if they don't exist
                var processingPath = Path.Combine(directory.FullName, "processing");
                var processedPath = Path.Combine(directory.FullName, "processed");
                var errorPath = Path.Combine(directory.FullName, "error");

                Directory.CreateDirectory(processingPath);
                Directory.CreateDirectory(processedPath);
                Directory.CreateDirectory(errorPath);

                Console.WriteLine("Press enter to exit.");

                while (true)
                {
                    // Get oldest file
                    var nextFile = directory
                        .GetFiles()
                        .OrderByDescending(f => f.LastWriteTime)
                        .FirstOrDefault();

                    var session = Guid.NewGuid().ToString("n");

                    // If no file was found, wait some time
                    if (nextFile == null)
                    {
                        if (Console.KeyAvailable)
                        {
                            if (Console.ReadKey().Key == ConsoleKey.Enter)
                            {
                                return 0;
                            }
                        }

                        await Task.Delay(TimeSpan.FromSeconds(5));
                        continue;
                    }

                    Console.WriteLine($"Found '{nextFile.Name}'");

                    // Attempting to move the file to the processing folder in order to lock it
                    var processingFilename = Path.Combine(processingPath, session + "." + nextFile.Name);
                    var processingFile = new FileInfo(processingFilename);

                    // If we can't move the file to the processing folder, we continue, which might retry the same file
                    try
                    {
                        nextFile.MoveTo(processingFilename);
                    }
                    catch
                    {
                        Console.WriteLine($"The file named '{nextFile.FullName}' couldn't be moved to the processing folder, skipping ...");
                        continue;
                    }

                    var arguments = $"{optionDriver.Value()} --server {optionServer.Value()} --client {optionClient.Value()} --jobs {processingFilename} --session {session}";

                    var process = new Process()
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

                    var outputBuilder = new StringBuilder();
                    var errorBuilder = new StringBuilder();

                    using (process)
                    {
                        process.OutputDataReceived += (_, e) =>
                        {
                            outputBuilder.AppendLine(e.Data);
                            Console.WriteLine(e.Data);
                        };

                        process.ErrorDataReceived += (_, e) =>
                        {
                            errorBuilder.AppendLine(e.Data);
                            Console.WriteLine(e.Data);
                        };

                        process.Start();

                        process.BeginOutputReadLine();
                        process.BeginErrorReadLine();

                        var start = DateTime.UtcNow;

                        while (true)
                        {
                            if ((DateTime.UtcNow - start > DriverTimeout))
                            {
                                Console.WriteLine("Driver timed out, skipping job");
                                process.Close();

                                try
                                {
                                    Console.WriteLine("Moving the file to errors");

                                    processingFile.MoveTo(Path.Combine(errorPath, processingFile.Name));
                                }
                                catch(Exception e)
                                {
                                    Console.WriteLine($"An error occured while move the file '{processingFile.FullName}' with the message: {e}");
                                }

                                continue;
                            }

                            if (process.HasExited)
                            {
                                break;
                            }

                            await Task.Delay(1000);
                        }

                        // Job succeeded?
                        if (process.ExitCode == 0)
                        {
                            Console.WriteLine("Job succeeded");

                            try
                            {
                                processingFile.MoveTo(Path.Combine(processedPath, processingFile.Name));

                                if (outputBuilder.Length > 0)
                                {
                                    File.WriteAllText(Path.Combine(processedPath, processingFile.Name + ".output.txt"), outputBuilder.ToString());
                                }

                                if (errorBuilder.Length > 0)
                                {
                                    File.WriteAllText(Path.Combine(processedPath, processingFile.Name + ".error.txt"), errorBuilder.ToString());
                                }
                            }
                            catch (Exception e)
                            {
                                Console.WriteLine($"An error occured while moving the file '{processingFile.Name}' to the processed folder: {e}");
                            }
                        }
                        else
                        {
                            Console.WriteLine("Job failed");

                            try
                            {
                                processingFile.MoveTo(Path.Combine(errorPath, processingFile.Name));

                                if (outputBuilder.Length > 0)
                                {
                                    File.WriteAllText(Path.Combine(errorPath, processingFile.Name + ".output.txt"), outputBuilder.ToString());
                                }

                                if (errorBuilder.Length > 0)
                                {
                                    File.WriteAllText(Path.Combine(errorPath, processingFile.Name + ".error.txt"), errorBuilder.ToString());
                                }
                            }
                            catch (Exception e)
                            {
                                Console.WriteLine($"An error occured while moving the file '{processingFile.Name}' to the error folder: {e}");
                            }
                        }                        
                    }
                }
            });

            return app.Execute(args);
        }

        private static string GetDotNetExecutable()
        {
            return RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? "dotnet.exe"
                : "dotnet"
                ;
        }
    }
}
