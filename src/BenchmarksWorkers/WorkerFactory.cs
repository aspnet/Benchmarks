// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.


using System;
using System.Collections.Generic;
using Benchmarks.ClientJob;
using BenchmarksWorkers.Workers;

namespace BenchmarksWorkers
{
    static public class WorkerFactory
    {
        public static Dictionary<Worker, Func<ClientJob, IWorker>> Workers = new Dictionary<Worker, Func<ClientJob, IWorker>>();
        public static Dictionary<Worker, Func<IResultsSerializer>> ResultSerializers = new Dictionary<Worker, Func<IResultsSerializer>>();

        static WorkerFactory()
        {
            // Wrk
            Workers[Worker.Wrk] = clientJob => new WrkWorker(clientJob);
            ResultSerializers[Worker.Wrk] = () => new WrkSerializer();
        }

        static public IWorker CreateWorker(ClientJob clientJob)
        {
            if (Workers.TryGetValue(clientJob.Client, out var workerFactory))
            {
                return workerFactory(clientJob);
            }

            return null;
        }

        static public IResultsSerializer CreateResultSerializer(ClientJob clientJob)
        {
            if (ResultSerializers.TryGetValue(clientJob.Client, out var serializerFactory))
            {
                return serializerFactory();
            }

            return null;
        }
    }
}
