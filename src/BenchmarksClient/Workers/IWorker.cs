// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Threading.Tasks;
using Benchmarks.ClientJob;

namespace BenchmarksClient.Workers
{
    public interface IWorker : IDisposable
    {
        string JobLogText { get; set; }

        Task StartJobAsync(ClientJob job);
        Task StopJobAsync();
        Task DisposeAsync();
    }
}
