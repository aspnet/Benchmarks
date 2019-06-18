// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using Benchmarks.ClientJob;

namespace BenchmarksClient.Workers
{
    static public class WorkerFactory
    {
        static public IWorker CreateWorker(ClientJob clientJob)
        {
            IWorker worker;
            switch (clientJob.Client)
            {
                case Worker.Wrk:
                    worker = new WrkWorker();
                    break;
                case Worker.Wrk2:
                    worker = new Wrk2Worker();
                    break;
                case Worker.SignalR:
                    worker = new SignalRWorker();
                    break;
                case Worker.Grpc:
                    worker = new GrpcWorker();
                    break;
                case Worker.Wait:
                    worker = new WaitWorker();
                    break;
                case Worker.H2Load:
                    worker = new H2LoadWorker();
                    break;
                case Worker.Bombardier:
                    worker = new BombardierWorker();
                    break;
                case Worker.BlazorIgnitor:
                    worker = new BlazorIgnitor();
                    break;
                default:
                    throw new InvalidOperationException($"Unknown worker {clientJob.Client}.");
            }
            return worker;
        }
    }
}
