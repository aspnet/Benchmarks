// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BenchmarkServer
{
    public static class ProcessUtil
    {
        public static ProcessResult Run(
            string filename, 
            string arguments, 
            TimeSpan? timeout = null, 
            string workingDirectory = null,
            bool throwOnError = true, 
            IDictionary<string, string> environmentVariables = null, 
            Action<string> outputDataReceived = null,
            bool log = false,
            Action onStart = null,
            CancellationToken cancellationToken = default(CancellationToken),
            bool captureOutput = false,
            bool captureError = false
            )
        {
            var standardOutput = new StringBuilder();
            var standardError = new StringBuilder();

            var logWorkingDirectory = workingDirectory ?? Directory.GetCurrentDirectory();

            if (log)
            {
                Log.WriteLine($"[{logWorkingDirectory}] {filename} {arguments}");
            }

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

                process.OutputDataReceived += (_, e) =>
                {
                    if (outputDataReceived != null)
                    {
                        outputDataReceived.Invoke(e.Data);
                    }

                    if (captureOutput)
                    {
                        standardOutput.AppendLine(e.Data);
                    }

                    if (log)
                    {
                        Log.WriteLine(e.Data);
                    }
                };

                process.ErrorDataReceived += (_, e) =>
                {
                    if (outputDataReceived != null)
                    {
                        outputDataReceived.Invoke(e.Data);
                    }

                    if (captureError)
                    {
                        standardError.AppendLine(e.Data);
                    }

                    Log.WriteLine("[STDERR] " + e.Data);
                };

                onStart?.Invoke();
                process.Start();

                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                var start = DateTime.UtcNow;

                while (true)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        StopProcess(process);
                        break;
                    }

                    if (timeout.HasValue && (DateTime.UtcNow - start > timeout.Value))
                    {
                        StopProcess(process);
                        break;
                    }
                    
                    if (process.HasExited)
                    {
                        break;
                    }

                    Thread.Sleep(500);
                }

                if (throwOnError && process.ExitCode != 0)
                {
                    throw new InvalidOperationException($"Command {filename} {arguments} returned exit code {process.ExitCode}");
                }
                

                if (log)
                {
                    Log.WriteLine($"Exit code: {process.ExitCode}");
                }

                return new ProcessResult(process.ExitCode, standardOutput.ToString(), standardError.ToString());
            }
        }

        public static T RetryOnException<T>(int retries, Func<T> operation)
        {
            var attempts = 0;
            do
            {
                try
                {
                    attempts++;
                    return operation();
                }
                catch (Exception e)
                {
                    if (attempts == retries + 1)
                    {
                        throw;
                    }

                    Log.WriteLine($"Attempt {attempts} failed: {e.Message}");
                }
            } while (true);
        }

        public static void StopProcess(Process process)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                Mono.Unix.Native.Syscall.kill(process.Id, Mono.Unix.Native.Signum.SIGINT);

                // Tentatively invoke SIGINT
                var waitForShutdownDelay = Task.Delay(TimeSpan.FromSeconds(5));
                while (!process.HasExited && !waitForShutdownDelay.IsCompletedSuccessfully)
                {
                    Thread.Sleep(200);
                }
            }

            if (!process.HasExited)
            {
                Log.WriteLine($"Forcing process to stop ...");
                process.CloseMainWindow();

                if (!process.HasExited)
                {
                    process.Kill();
                }

                process.Dispose();

                do
                {
                    Log.WriteLine($"Waiting for process {process.Id} to stop ...");

                    Thread.Sleep(1000);

                    try
                    {
                        process = Process.GetProcessById(process.Id);
                        process.Refresh();
                    }
                    catch
                    {
                        process = null;
                    }

                } while (process != null && !process.HasExited);
            }
        }

        internal static void TryKillAllProcesses(HashSet<int> except)
        {
            foreach(var process in Process.GetProcesses().Where(process => !except.Contains(process.Id)))
            {
                Log.WriteLine($"Trying to kill {process.Id} {process.ProcessName}");

                try
                {
                    process.KillTree(TimeSpan.FromSeconds(3));
                }
                catch { } // swallow the exception on purpose
            }
        }

        internal static void KillTree(this Process process, TimeSpan timeout)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Run("taskkill", $"/T /F /PID {process.Id}", timeout);
            }
            else
            {
                var children = new HashSet<int>();
                GetAllChildIdsUnix(process.Id, children, timeout);
                foreach (var childId in children)
                {
                    KillProcessUnix(childId, timeout);
                }
                KillProcessUnix(process.Id, timeout);
            }
        }

        private static void KillProcessUnix(int processId, TimeSpan timeout) => Run("kill", $"-TERM {processId}", timeout);

        private static void GetAllChildIdsUnix(int parentId, HashSet<int> children, TimeSpan timeout)
        {
            var runResult = Run("pgrep", $"-P {parentId}", timeout, captureOutput: true);

            if (runResult.ExitCode != 0 || string.IsNullOrEmpty(runResult.StandardOutput))
                return;

            using (var reader = new StringReader(runResult.StandardOutput))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (int.TryParse(line, out int id) && !children.Contains(id))
                    {
                        children.Add(id);
                        // Recursively get the children
                        GetAllChildIdsUnix(id, children, timeout);
                    }
                }
            }
        }
    }
}
