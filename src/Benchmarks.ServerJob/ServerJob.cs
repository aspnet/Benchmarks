// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Repository;

namespace Benchmarks.ServerJob
{
    public class ServerJob : IIdentifiable
    {
        public int Id { get; set; }

        public string ConnectionFilter { get; set; }

        [JsonConverter(typeof(StringEnumConverter))]
        public Hardware? Hardware { get; set; }

        [JsonConverter(typeof(StringEnumConverter))]
        public OperatingSystem? OperatingSystem { get; set; }

        public int? KestrelThreadCount { get; set; }

        public bool? KestrelThreadPoolDispatching { get; set; }

        [JsonConverter(typeof(StringEnumConverter))]
        public KestrelTransport? KestrelTransport { get; set; }

        public string Scenario { get; set; }

        [JsonConverter(typeof(StringEnumConverter))]
        public Scheme Scheme { get; set; }
        public int Port { get; set; } = 5000;
        public string Path { get; set; }
        public string AspNetCoreVersion { get; set; }
        
        // Delay from the process started to the console receiving "Application started"
        public TimeSpan StartupMainMethod { get; set; }
        public List<ServerCounter> ServerCounters { get; set; } = new List<ServerCounter>();
        public IList<Source> Sources { get; set; } = new List<Source>();

        public Source Source { get; set; } = new Source();
        public string Arguments { get; set; }

        [JsonConverter(typeof(StringEnumConverter))]
        public ServerState State { get; set; }

        public string Url { get; set; }

        [JsonConverter(typeof(StringEnumConverter))]
        public WebHost WebHost { get; set; }
    }
}
