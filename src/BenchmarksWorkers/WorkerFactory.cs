// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.


using System;
using System.Collections.Generic;
using Benchmarks.ClientJob;

namespace BenchmarksWorkers
{
    static public class WorkerFactory
    {
        public static Dictionary<string, Func<ClientJob, IWorker>> Workers = new Dictionary<string, Func<ClientJob, IWorker>>();
        public static Dictionary<string, Func<IResultsSerializer>> ResultSerializers = new Dictionary<string, Func<IResultsSerializer>>();

        static public IWorker CreateWorker(ClientJob clientJob)
        {
            if (Workers.TryGetValue(clientJob.ClientName, out var workerFactory))
            {
                return workerFactory(clientJob);
            }

            return null;
        }

        static public IResultsSerializer CreateResultSerializer(ClientJob clientJob)
        {
            if (ResultSerializers.TryGetValue(clientJob.ClientName, out var serializerFactory))
            {
                return serializerFactory();
            }

            return null;
        }
    }
}
