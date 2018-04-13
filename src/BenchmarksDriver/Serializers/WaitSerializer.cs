// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Threading.Tasks;
using Benchmarks.ClientJob;
using Benchmarks.ServerJob;

namespace BenchmarksDriver.Serializers
{
    public class WaitSerializer : IResultsSerializer
    {        
        public void Dispose()
        {
        }

        public void ComputeAverages(Statistics average, IEnumerable<Statistics> samples)
        {
        }

        public Task InitializeDatabaseAsync(string connectionString, string tableName)
        {
            return Task.CompletedTask;
        }

        public Task WriteJobResultsToSqlAsync(ServerJob serverJob, ClientJob clientJob, string connectionString, string tableName, string path, string session, string description, Statistics statistics, bool longRunning)
        {
            return Task.CompletedTask
        }
    }
}