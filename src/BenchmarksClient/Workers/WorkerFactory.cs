// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.


using System;
using System.Collections.Generic;
using Benchmarks.ClientJob;

namespace BenchmarksClient.Workers
{
    static public class WorkerFactory
    {
        public static Dictionary<Worker, Func<ClientJob, IWorker>> Workers = new Dictionary<Worker, Func<ClientJob, IWorker>>();

        static WorkerFactory()
        {
            // Wrk
            Workers[Worker.Wrk] = clientJob => new WrkWorker(clientJob);

            // SignalR
            Workers[Worker.SignalR] = clientJob => new SignalRWorker(clientJob);
        }

        static public IWorker CreateWorker(ClientJob clientJob)
        {
            if (Workers.TryGetValue(clientJob.Client, out var workerFactory))
            {
                return workerFactory(clientJob);
            }

            return null;
        }
    }
}
