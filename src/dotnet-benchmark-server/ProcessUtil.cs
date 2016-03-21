using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace BenchmarkServer
{
    public static class ProcessUtil
    {
        public static string Run(string filename, string arguments, string workingDirectory = null,
            bool throwOnError = true)
        {
            Log.WriteLine($"[{workingDirectory ?? Directory.GetCurrentDirectory()}] {filename} {arguments}");

            var process = new Process()
            {
                StartInfo =
                {
                    FileName = filename,
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                },
            };

            if (workingDirectory != null)
            {
                process.StartInfo.WorkingDirectory = workingDirectory;
            }

            process.Start();
            process.WaitForExit();

            if (throwOnError && process.ExitCode != 0)
            {
                throw new InvalidOperationException($"Command {filename} {arguments} returned exit code {process.ExitCode}");
            }

            return process.StandardOutput.ReadToEnd();
        }
    }
}
