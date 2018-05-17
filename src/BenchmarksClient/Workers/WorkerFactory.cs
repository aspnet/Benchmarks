﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Benchmarks.ClientJob;

namespace BenchmarksClient.Workers
{
    static public class WorkerFactory
    {
        static public IWorker CreateWorker(ClientJob clientJob)
        {
            IWorker worker = null;
            switch (clientJob.Client)
            {
                case Worker.Wrk:
                    worker = new WrkWorker();
                    break;
                case Worker.SignalR:
                    worker = new SignalRWorker();
                    break;
                case Worker.Wait:
                    worker = new WaitWorker();
                    break;
            }
            return worker;
        }
    }
}
