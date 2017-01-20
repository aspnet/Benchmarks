// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Repository;

namespace Benchmarks.ClientJob
{
    public class ClientJob : IIdentifiable
    {
        public ClientJob()
        {
        }

        public ClientJob(ClientJob clientJob)
        {
            Id = clientJob.Id;
            Threads = clientJob.Threads;
            Connections = clientJob.Connections;
            Duration = clientJob.Duration;
            PipelineDepth = clientJob.PipelineDepth;
            Headers = clientJob.Headers;
            ServerBenchmarkUri = clientJob.ServerBenchmarkUri;
            State = clientJob.State;
            RequestsPerSecond = clientJob.RequestsPerSecond;
            Output = clientJob.Output;
            Error = clientJob.Error;
            Method = clientJob.Method;
        }

        public int Id { get; set; }

        public int Threads { get; set; }

        public int Connections { get; set; }

        public int Duration { get; set; }

        public int PipelineDepth { get; set; }

        public string[] Headers { get; set; }

        public string ServerBenchmarkUri { get; set; }

        [JsonConverter(typeof(StringEnumConverter))]
        public ClientState State { get; set; }

        public double RequestsPerSecond { get; set; }

        public string Output { get; set; }

        public string Error { get; set; }

        public string Method { get; set; } = "GET";
    }
}
