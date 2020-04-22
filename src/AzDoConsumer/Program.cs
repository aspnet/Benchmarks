// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Azure.Messaging.ServiceBus;
using McMaster.Extensions.CommandLineUtils;

namespace AzDoConsumer
{
    public class Program
    {
        private static TimeSpan TaskLogFeedDelay = TimeSpan.FromSeconds(2);

        private static string ConnectionString { get; set; }
        private static string Queue { get; set; }
        private static string Path { get; set; }
        private static string Args { get; set; }

        public static int Main(string[] args)
        {
            // Replace arguments starting with "env:" by the value in the environment variables
            for (var i = 0; i < args.Length; i++)
            {
                if (args[i].StartsWith("env:", StringComparison.OrdinalIgnoreCase))
                {
                    args[i] = Environment.GetEnvironmentVariable(args[i].Substring(4));
                }
            }

            var app = new CommandLineApplication();

            app.HelpOption("-h|--help");
            var connectionStringOption = app.Option("-c|--connection-string <string>", "The Azure Service Bus connection string", CommandOptionType.SingleValue).IsRequired();
            var queueOption = app.Option("-q|--queue <string>", "The Azure Service Bus queue name", CommandOptionType.SingleValue).IsRequired();
            var pathOption = app.Option("-e|--executable <PATH>", "The application file path", CommandOptionType.SingleValue).IsRequired();
            var argsOption = app.Option("-a|--args <args>", "The extra arguments to pass", CommandOptionType.SingleValue);

            app.OnExecuteAsync(async cancellationToken =>
            {
                ConnectionString = connectionStringOption.Value();
                Queue = queueOption.Value();
                Path = pathOption.Value();
                Args = argsOption.HasValue() ? argsOption.Value() : "";

                if (!File.Exists(Path))
                {
                    Console.WriteLine($"The driver could not be found at: '{Path}'");
                    return;
                }

                var client = new ServiceBusClient(ConnectionString);
                var processor = client.CreateProcessor(Queue, new ServiceBusProcessorOptions
                {
                    AutoComplete = false,
                    MaxConcurrentCalls = 1, // Process one message at a time
                });

                // Whenever a message is available on the queue
                processor.ProcessMessageAsync += async args =>
                {
                    Console.WriteLine("Processing message: ");
                    Console.WriteLine(args.Message.ToString());

                    var message = args.Message;

                    JobPayload jobPayload;
                    DevopsMessage devopsMessage = null;
                    DriverJob driverJob = null;

                    try
                    {
                        // The Body contains the parameters for the application to run
                        jobPayload = JobPayload.Deserialize(message.Body.ToArray());

                        Console.WriteLine("Received payload: " + jobPayload.RawPayload);

                        // The DevopsMessage does the communications with AzDo
                        devopsMessage = new DevopsMessage(message);

                        await devopsMessage.SendTaskStartedEventAsync();

                        // The arguments are built from the Task payload and the ones
                        // set on the command line
                        var arguments = String.Join(' ', jobPayload.Args) + " " + Args;

                        // The DriverJob manages the application's lifetime and standard output
                        driverJob = new DriverJob(Path, arguments);

                        driverJob.OnStandardOutput = log => Console.WriteLine(log);

                        Console.WriteLine("Processing...");

                        driverJob.Start();

                        // Pump application standard output while it's running
                        while (driverJob.IsRunning)
                        {
                            if ((DateTime.UtcNow - driverJob.StartTimeUtc) > jobPayload.Timeout)
                            {
                                throw new Exception("Job timed out");
                            }

                            var logs = driverJob.FlushStandardOutput().ToArray();

                            // Send any page of logs to the AzDo task log feed
                            if (logs.Any())
                            {
                                await devopsMessage.SendTaskLogFeedsAsync(String.Join("\r\n", logs));
                            }

                            await Task.Delay(TaskLogFeedDelay);
                        }

                        // Mark the task as completed
                        await devopsMessage.SendTaskCompletedEventAsync(succeeded: true);

                        // Create a task log entry
                        var taskLogObjectString = await devopsMessage?.CreateTaskLogAsync();

                        var taskLogObject = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(taskLogObjectString);

                        var taskLogId = taskLogObject["id"].ToString();

                        await devopsMessage?.AppendToTaskLogAsync(taskLogId, driverJob.OutputBuilder.ToString());

                        // Attach task log to the timeline record
                        await devopsMessage?.UpdateTaskTimelineRecordAsync(taskLogObjectString);

                        // Mark the message as completed
                        await args.CompleteAsync(message);

                        driverJob.Stop();

                        Console.WriteLine("Job completed");
                    }
                    catch (Exception e)
                    {
                        await devopsMessage?.SendTaskCompletedEventAsync(succeeded: false);

                        await args.AbandonAsync(message);

                        Console.WriteLine("Job failed: " + e.Message);
                    }
                    finally
                    {
                        driverJob?.Dispose();
                    }
                };

                processor.ProcessErrorAsync += args =>
                {
                    Console.WriteLine("Process error: " + args.Exception.Message);

                    throw args.Exception;
                };

                await processor.StartProcessingAsync();

                Console.WriteLine("Press ENTER to exit...");
                Console.ReadLine();
            });

            return app.Execute(args);
        }
    }
}
