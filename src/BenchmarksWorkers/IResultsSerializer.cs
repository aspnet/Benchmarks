// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Benchmarks.ClientJob;
using Benchmarks.ServerJob;

namespace BenchmarksWorkers
{
    public interface IResultsSerializer : IDisposable
    {
        /// <summary>
        /// Creates the necessary tables if a database is required.
        /// </summary>
        Task InitializeDatabaseAsync(string connectionString, string tableName);

        /// <summary>
        /// Writes the job results to the database.
        /// </summary>
        Task WriteJobResultsToSqlAsync(ServerJob serverJob,
            ClientJob clientJob,
            string connectionString,
            string tableName,
            string path,
            string session,
            string description,
            Statistics statistics,
            bool longRunning);

        /// <summary>
        /// Calculates the average values for the custom samples measures
        /// </summary>
        void ComputeAverages(Statistics average, IEnumerable<Statistics> samples);
    }
}
