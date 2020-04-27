// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
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

        public static int Main(string[] args)
        {
            var app = new CommandLineApplication();

            app.HelpOption("-h|--help");
            var connectionStringOption = app.Option("-c|--connection-string <string>", "The Azure Service Bus connection string", CommandOptionType.SingleValue).IsRequired();
            var queueOption = app.Option("-q|--queue <string>", "The Azure Service Bus queue name", CommandOptionType.SingleValue).IsRequired();
            var jobDefinitionsPathOption = app.Option("-j|--jobs <path>", "The path for the job definitions file", CommandOptionType.SingleValue).IsRequired();
            
            app.OnExecuteAsync(async cancellationToken =>
            {
                if (!File.Exists(jobDefinitionsPathOption.Value()))
                {
                    Console.WriteLine($"The file '{0}' could not be found", jobDefinitionsPathOption.Value());
                    return;
                }

                var jobDefinitions = JsonSerializer.Deserialize<JobDefinitions>(File.ReadAllText(jobDefinitionsPathOption.Value()), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                ConnectionString = connectionStringOption.Value();

                // Substitute with ENV value if it exists
                if (!String.IsNullOrEmpty(Environment.GetEnvironmentVariable(ConnectionString)))
                {
                    ConnectionString = Environment.GetEnvironmentVariable(ConnectionString);
                }

                Queue = queueOption.Value();

                // Substitute with ENV value if it exists
                if (!String.IsNullOrEmpty(Environment.GetEnvironmentVariable(Queue)))
                {
                    Queue = Environment.GetEnvironmentVariable(Queue);
                }

                foreach (var job in jobDefinitions.Jobs)
                {
                    if (!File.Exists(job.Value.Executable))
                    {
                        Console.WriteLine($"The executable for the job '{job.Key}' could not be found at '{job.Value.Executable}'");
                        return;
                    }
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
                    Console.WriteLine("Processing message '{0}'", args.Message.ToString());

                    var message = args.Message;

                    JobPayload jobPayload;
                    DevopsMessage devopsMessage = null;
                    Job driverJob = null;

                    try
                    {
                        // The DevopsMessage does the communications with AzDo
                        devopsMessage = new DevopsMessage(message);

                        // The Body contains the parameters for the application to run
                        jobPayload = JobPayload.Deserialize(message.Body.ToArray());


                        if (!jobDefinitions.Jobs.TryGetValue(jobPayload.Name, out var job))
                        {
                            throw new Exception("Invalid job name: " + jobPayload.Name);
                        }

                        await devopsMessage.SendTaskStartedEventAsync();

                        // The arguments are built from the Task payload and the ones
                        // set on the command line
                        var allArguments = jobPayload.Args.Union(job.Arguments).ToArray();

                        // Convert any arguments with the custom bindings

                        foreach (var binding in job.Bindings.Keys)
                        {
                            if (job.Bindings[binding].StartsWith("env:"))
                            {
                                job.Bindings[binding] = Environment.GetEnvironmentVariable(binding.Substring(4));
                            }
                        }

                        // Create protected arguments string by hiding binding values
                        for (var i = 0; i < allArguments.Length; i++)
                        {
                            var argument = allArguments[i];

                            if (job.Bindings.ContainsKey(argument))
                            {
                                allArguments[i] = "****";
                            }
                        }

                        var sanitizedArguments = String.Join(' ', allArguments);

                        for (var i = 0; i < allArguments.Length; i++)
                        {
                            var argument = allArguments[i];

                            if (job.Bindings.ContainsKey(argument))
                            {
                                allArguments[i] = job.Bindings[argument];
                            }
                        }

                        var arguments = String.Join(' ', allArguments);

                        Console.WriteLine("Invoking application with arguments: " + sanitizedArguments);

                        // The DriverJob manages the application's lifetime and standard output
                        driverJob = new Job(job.Executable, arguments);

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
                        await devopsMessage.SendTaskCompletedEventAsync(succeeded: driverJob.WasSuccessful);

                        // Create a task log entry
                        var taskLogObjectString = await devopsMessage?.CreateTaskLogAsync();

                        var taskLogObject = JsonSerializer.Deserialize<Dictionary<string, object>>(taskLogObjectString);

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
                    Console.WriteLine("Process error: " + args.Exception.ToString());

                    return Task.CompletedTask;
                };

                await processor.StartProcessingAsync();

                Console.WriteLine("Press ENTER to exit...");
                Console.ReadLine();
            });

            return app.Execute(args);
        }
    }
}
