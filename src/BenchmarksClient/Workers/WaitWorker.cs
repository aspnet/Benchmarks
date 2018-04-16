// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Benchmarks.ClientJob;

namespace BenchmarksClient.Workers
{
    public class WaitWorker : IWorker
    {
        private ClientJob _job;
        private Task _task;
        private CancellationTokenSource _cts;

        public string JobLogText { get; set; }

        public WaitWorker(ClientJob clientJob)
        {
            _job = clientJob;            
        }

        public Task StartAsync()
        {
            _job.State = ClientState.Running;
            _job.LastDriverCommunicationUtc = DateTime.UtcNow;

            _cts = new CancellationTokenSource();

            _task = Task.Run(async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(_job.Duration));
                _job.State = ClientState.Completed;
            }, _cts.Token);

            return Task.CompletedTask;
        }

        public async Task StopAsync()
        {
            try
            {
                _cts.Cancel();

                if (_task != null)
                {
                    await _task;
                }
            }
            finally
            {
                _task.Dispose();
                _cts.Dispose();
            }

            return;
        }

        public void Dispose()
        {
        }

        private static void Log(string message)
        {
            var time = DateTime.Now.ToString("hh:mm:ss.fff");
            Console.WriteLine($"[{time}] {message}");
        }
    }
}