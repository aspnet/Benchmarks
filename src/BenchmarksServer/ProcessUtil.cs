// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace BenchmarkServer
{
    public static class ProcessUtil
    {
        public static ProcessResult Run(string filename, string arguments, TimeSpan? timeout = null, string workingDirectory = null,
            bool throwOnError = true, IDictionary<string, string> environmentVariables = null, bool useShellExecute = false)
        {
            var logWorkingDirectory = workingDirectory ?? Directory.GetCurrentDirectory();
            Log.WriteLine($"[{logWorkingDirectory}] {filename} {arguments}");

            var process = new Process()
            {
                StartInfo =
                {
                    FileName = filename,
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = useShellExecute,
                },
            };

            using (process)
            {
                if (workingDirectory != null)
                {
                    process.StartInfo.WorkingDirectory = workingDirectory;
                }

                if (environmentVariables != null)
                {
                    foreach (var kvp in environmentVariables)
                    {
                        process.StartInfo.Environment.Add(kvp);
                    }
                }

                var outputBuilder = new StringBuilder();
                process.OutputDataReceived += (_, e) =>
                {
                    outputBuilder.AppendLine(e.Data);
                    Log.WriteLine(e.Data);
                };

                var errorBuilder = new StringBuilder();
                process.ErrorDataReceived += (_, e) =>
                {
                    errorBuilder.AppendLine(e.Data);
                    Log.WriteLine(e.Data);
                };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                if (timeout.HasValue)
                {
                    process.WaitForExit((int)timeout.Value.TotalMilliseconds);
                }
                else
                {
                    process.WaitForExit();
                }

                if (throwOnError && process.ExitCode != 0)
                {
                    throw new InvalidOperationException($"Command {filename} {arguments} returned exit code {process.ExitCode}");
                }

                return new ProcessResult(outputBuilder.ToString(), errorBuilder.ToString(), process.ExitCode);
            }            
        }
    }
}
