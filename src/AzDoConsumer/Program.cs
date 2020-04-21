// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Azure.Messaging.ServiceBus;
using McMaster.Extensions.CommandLineUtils;

namespace AzDoConsumer
{
    public class Program
    {
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
                    MaxConcurrentCalls = 1, // process one message at a time
                });

                processor.ProcessMessageAsync += async args =>
                {
                    var message = args.Message;

                    JobPayload jobPayload;
                    DevopsMessage devopsMessage = null;

                    try
                    {
                        jobPayload = JobPayload.Deserialize(message.Body.ToArray());
                        devopsMessage = new DevopsMessage(message);

                        var driverJob = new DriverJob();

                        Console.WriteLine("Received message: " + jobPayload.Message);

                        await devopsMessage.SendTaskStartedEventAsync();

                        // var result = driverJob.Run(Path, String.Join(' ', jobPayload.Args) + " " + Args);

                        // Console.WriteLine(result);

                        await devopsMessage.SendTaskLogFeedsAsync("line 1", "line 2");

                        Console.WriteLine("Processing...");

                        await Task.Delay(10000);

                        await devopsMessage.SendTaskCompletedEventAsync(succeeded: true);

                        await args.CompleteAsync(message);

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
                        // Upload the task logs. Create task log and append the all logs to task log.
                        
                        // Create task log entry
                        var taskLogObjectString = await devopsMessage?.CreateTaskLogAsync();

                        //var taskLogObject = System.Text.Json.JsonSerializer.Deserialize(taskLogObjectString);

                        //await devopsMessage?.AppendToTaskLogAsync(taskLogId, "message 1", "message 2");

                        // Attache task log to the timeline record
                        await devopsMessage?.UpdateTaskTimelineRecordAsync(taskLogObjectString);
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
