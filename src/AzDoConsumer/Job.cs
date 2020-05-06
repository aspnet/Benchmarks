// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace AzDoConsumer
{
    public class Job : IDisposable
    {
        private Process _process;

        private ConcurrentQueue<string> _standardOutput = new ConcurrentQueue<string>();

        private ConcurrentQueue<string> _standardError = new ConcurrentQueue<string>();

        public StringBuilder OutputBuilder { get; private set; } = new StringBuilder();
        
        public StringBuilder ErrorBuilder { get; private set; } = new StringBuilder();

        public Action<string> OnStandardOutput { get; set; }

        public Action<string> OnStandardError { get; set; }

        public DateTime StartTimeUtc { get; private set; }

        public Job (string applicationPath, string arguments, string workingDirectory = null)
        {
            _process = new Process()
            {
                StartInfo =
                {
                    FileName = applicationPath,
                    Arguments = arguments,
                    WorkingDirectory = workingDirectory ?? Path.GetDirectoryName(applicationPath),
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                },
            };
        }

        public void Start()
        {
            if (_process == null)
            {
                throw new Exception("Can't reuse disposed job");
            }

            _process.OutputDataReceived += (_, e) =>
            {
                // e.Data is null to signal end of stream
                if (e.Data != null)
                {
                    _standardOutput.Enqueue(e.Data);
                    OnStandardOutput?.Invoke(e.Data);
                    OutputBuilder.AppendLine(e.Data);
                }
            };

            _process.ErrorDataReceived += (_, e) =>
            {
                // e.Data is null to signal end of stream
                if (e.Data != null)
                {
                    _standardError.Enqueue(e.Data);
                    OnStandardError?.Invoke(e.Data);
                    ErrorBuilder.AppendLine(e.Data);
                }
            };

            StartTimeUtc = DateTime.UtcNow;

            _process.Start();

            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();
        }

        public void Stop()
        {
            try
            {
                if (_process != null)
                {
                    if (!_process.HasExited)
                    {
                        _process.Close();
                    }

                    if (!_process.HasExited)
                    {
                        _process.CloseMainWindow();
                    }

                    if (!_process.HasExited)
                    {
                        _process.Kill();
                    }
                }
            }
            catch (InvalidOperationException e)
            {
                // Ignore if the application is already stopped:
                //Process error: System.InvalidOperationException: No process is associated with this object.
                //   at System.Diagnostics.Process.EnsureState(State state)
                //   at System.Diagnostics.Process.get_HasExited()
            }
        }

        public IEnumerable<string> FlushStandardOutput()
        {
            while (_standardOutput.TryDequeue(out var result))
            {
                yield return result;
            }
        }

        public IEnumerable<string> FlushStandardError()
        {
            while (_standardOutput.TryDequeue(out var result))
            {
                yield return result;
            }
        }

        public bool WasSuccessful => _process != null && _process.ExitCode == 0;

        public bool IsRunning => _process != null && !_process.HasExited;

        public void Dispose()
        {
            Stop();

            OnStandardOutput = null;
            OnStandardError = null;
            _standardError = null;
            _standardOutput = null;
            OutputBuilder = null;
            ErrorBuilder = null;
            _process?.Dispose();
            _process = null;
        }

    }
}
