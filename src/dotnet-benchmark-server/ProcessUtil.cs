// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace BenchmarkServer
{
    public static class ProcessUtil
    {
        public static string Run(string filename, string arguments, string workingDirectory = null,
            bool throwOnError = true)
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
                    UseShellExecute = false,
                },
            };

            if (workingDirectory != null)
            {
                process.StartInfo.WorkingDirectory = workingDirectory;
            }

            var outputBuilder = new StringBuilder();
            process.OutputDataReceived += (_, e) =>
            {
                outputBuilder.AppendLine(e.Data);
                Log.WriteLine($"[{logWorkingDirectory}] [{filename} {arguments}] {e.Data}");
            };

            process.ErrorDataReceived += (_, e) =>
            {
                Log.WriteLine($"[{logWorkingDirectory}] [{filename} {arguments}] {e.Data}");
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            process.WaitForExit();

            if (throwOnError && process.ExitCode != 0)
            {
                throw new InvalidOperationException($"Command {filename} {arguments} returned exit code {process.ExitCode}");
            }

            return outputBuilder.ToString();
        }
    }
}
