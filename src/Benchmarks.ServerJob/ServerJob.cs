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
        public string AspNetCoreVersion { get; set; } = "Latest";
        public string RuntimeVersion { get; set; } = "Latest";
        public string SdkVersion { get; set; }
        public Database Database { get; set; } = Database.None;
        
        // Delay from the process started to the console receiving "Application started"
        public TimeSpan StartupMainMethod { get; set; }
        private List<ServerCounter> _serverCounter = new List<ServerCounter>();
        public IEnumerable<ServerCounter> ServerCounters => _serverCounter;

        public ServerJob AddServerCounter(ServerCounter counter)
        {
            lock (this)
            {
                _serverCounter.Add(counter);
                return this;
            }
        }

        public ServerJob ClearServerCounters()
        {
            lock (this)
            {
                _serverCounter.Clear();
                return this;
            }
        }

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
        public WebHost WebHost { get; set; } = WebHost.KestrelLibuv;

        public bool UseRuntimeStore { get; set; }

        public List<Attachment> Attachments { get; set; } = new List<Attachment>();

        public DateTime LastDriverCommunicationUtc { get; set; } = DateTime.UtcNow;

        public bool Collect { get; set; }
        public string CollectArguments { get; set; }
        public string PerfViewTraceFile { get; set; }
        public string BasePath { get; set; }
        public int ProcessId { get; set; }
        public Dictionary<string, string> EnvironmentVariables { get; set; } = new Dictionary<string, string>();
    }
}
