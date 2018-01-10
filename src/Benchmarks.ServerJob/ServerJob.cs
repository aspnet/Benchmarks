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

        public string HardwareVersion { get; set; }

        [JsonConverter(typeof(StringEnumConverter))]
        public OperatingSystem? OperatingSystem { get; set; }

        public int? KestrelThreadCount { get; set; }

        public string Scenario { get; set; }

        [JsonConverter(typeof(StringEnumConverter))]
        public Scheme Scheme { get; set; }
        public int Port { get; set; } = 5000;
        public string Path { get; set; } = "/";
        public string AspNetCoreVersion { get; set; } = "2.1.0-*";
        public Database Database { get; set; } = Database.None;
        
        // Delay from the process started to the console receiving "Application started"
        public TimeSpan StartupMainMethod { get; set; }
        public List<ServerCounter> ServerCounters { get; set; } = new List<ServerCounter>();

        /// <summary>
        /// The source information for references packages
        /// </summary>
        public IList<Source> ReferenceSources { get; set; } = new List<Source>();

        /// <summary>
        /// The source information for the benchmarked application
        /// </summary>
        public Source Source { get; set; } = new Source();

        public string Arguments { get; set; }

        [JsonConverter(typeof(StringEnumConverter))]
        public ServerState State { get; set; }

        public string Url { get; set; }

        [JsonConverter(typeof(StringEnumConverter))]
        public WebHost WebHost { get; set; }

        public bool UseRuntimeStore { get; set; }
        public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(5);

        public Attachment[] Attachments { get; set; }

        public DateTime LastDriverCommunicationUtc { get; set; } = DateTime.UtcNow;

        public bool Collect { get; set; }
        public string CollectArguments { get; set; }
        public string PerfViewTraceFile { get; set; }
    }
}
