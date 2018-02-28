﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
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
            Connections = clientJob.Connections;
            Duration = clientJob.Duration;
            ClientProperties = clientJob.ClientProperties;
            ClientName = clientJob.ClientName;
            Headers = clientJob.Headers;
            ServerBenchmarkUri = clientJob.ServerBenchmarkUri;
            Query = clientJob.Query;
            State = clientJob.State;
            RequestsPerSecond = clientJob.RequestsPerSecond;
            Output = clientJob.Output;
            Error = clientJob.Error;
            Method = clientJob.Method;
            SkipStartupLatencies = clientJob.SkipStartupLatencies;
        }

        public int Id { get; set; }

        public string ClientName { get; set; }

        public int Connections { get; set; } = 256;

        public int Duration { get; set; } = 15;
        public int Warmup { get; set; } = 15;

        public Dictionary<string, string> ClientProperties { get; set; } = new Dictionary<string, string>();

        public Dictionary<string, string> Headers { get; set; } = new Dictionary<string, string>();

        public string ServerBenchmarkUri { get; set; }

        public string Query { get; set; }

        [JsonConverter(typeof(StringEnumConverter))]
        public ClientState State { get; set; }

        public double RequestsPerSecond { get; set; }
        public int Requests { get; set; }
        public TimeSpan ActualDuration { get; set; }
        public int SocketErrors { get; set; }
        public int BadResponses { get; set; }
        public Latency Latency { get;set; } = new Latency();

        public string Output { get; set; }

        public string Error { get; set; }

        public string Method { get; set; } = "GET";
        public bool SkipStartupLatencies { get; set; }

        // Latency of first request
        public TimeSpan LatencyFirstRequest { get; set; }

        // Latency with a single connection
        public TimeSpan LatencyNoLoad { get; set; }
        public DateTime LastDriverCommunicationUtc { get; set; } = DateTime.UtcNow;
    }
}
