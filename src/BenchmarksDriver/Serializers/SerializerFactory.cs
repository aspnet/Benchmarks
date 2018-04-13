// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.


using System;
using System.Collections.Generic;
using Benchmarks.ClientJob;

namespace BenchmarksDriver.Serializers
{
    static public class WorkerFactory
    {
        public static Dictionary<Worker, Func<IResultsSerializer>> ResultSerializers = new Dictionary<Worker, Func<IResultsSerializer>>();

        static WorkerFactory()
        {
            // Wrk
            ResultSerializers[Worker.Wrk] = () => new WrkSerializer();

            // SignalR
            ResultSerializers[Worker.SignalR] = () => new SignalRSerializer();

            // SignalR
            ResultSerializers[Worker.Wait] = () => new WaitSerializer();
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
