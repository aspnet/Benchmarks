// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;
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
            ScriptName = clientJob.ScriptName;
            PipelineDepth = clientJob.PipelineDepth;
            Headers = clientJob.Headers;
            ServerBenchmarkUri = clientJob.ServerBenchmarkUri;
            Query = clientJob.Query;
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

        public string ScriptName { get; set; }

        public int PipelineDepth { get; set; }

        public Dictionary<string, string> Headers { get; set; } = new Dictionary<string, string>();

        public string ServerBenchmarkUri { get; set; }

        public string Query { get; set; }

        [JsonConverter(typeof(StringEnumConverter))]
        public ClientState State { get; set; }

        public double RequestsPerSecond { get; set; }
        public Latency Latency { get;set; } = new Latency();

        public string Output { get; set; }

        public string Error { get; set; }

        public string Method { get; set; } = "GET";
    }
}
