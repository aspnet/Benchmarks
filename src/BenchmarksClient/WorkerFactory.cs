// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Net.Http;
using Benchmarks.ClientJob;
using BenchmarksClient.Workers;

namespace BenchmarksClient
{
    static public class WorkerFactory
    {
        static public bool TryCreate(ClientJob clientJob, out IWorker worker, out string errors)
        {
            errors = null;
            worker = null;

            switch (clientJob.ClientName)
            {
                case "wrk":
                    worker = new WrkWorker(clientJob);
                    return true;
                default:
                    errors = $"'{clientJob.ClientName}' is not a valid worker";
                    return false;
            }
        }
    }
}
