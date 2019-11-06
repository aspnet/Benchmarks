﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Repository;

namespace Benchmarks.ServerJob
{
    public class ServerJob : IIdentifiable
    {
        public int DriverVersion { get; set; } = 0;

        public int ServerVersion { get; set; } = 1;

        public int Id { get; set; }

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
        public string ReadyStateText { get; set; }
        public bool IsConsoleApp { get; set; }
        public string AspNetCoreVersion { get; set; } = "Latest";
        public string RuntimeVersion { get; set; } = "Latest";
        public string SdkVersion { get; set; } = "Latest";
        public bool NoGlobalJson { get; set; }
        public Database Database { get; set; } = Database.None;

        // Delay from the process started to the console receiving "Application started"
        public TimeSpan StartupMainMethod { get; set; }
        public TimeSpan BuildTime { get; set; }
        public long PublishedSize { get; set; }

        private IList<ServerCounter> _serverCounter = new List<ServerCounter>();

        public IReadOnlyCollection<ServerCounter> ServerCounters
        {
            get
            {
                lock (this)
                {
                    return _serverCounter.ToArray();
                }
            }

            set
            {
                lock (this)
                {
                    _serverCounter = new List<ServerCounter>(value);
                }
            }
        }

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
        /// The source information for the benchmarked application
        /// </summary>
        public Source Source { get; set; } = new Source();

        public string Arguments { get; set; }
        public bool NoArguments { get; set; } = false;

        [JsonConverter(typeof(StringEnumConverter))]
        public ServerState State { get; set; }

        public string Url { get; set; }

        [JsonConverter(typeof(StringEnumConverter))]
        public WebHost WebHost { get; set; } = WebHost.KestrelSockets;

        public bool UseRuntimeStore { get; set; }

        public List<Attachment> Attachments { get; set; } = new List<Attachment>();
        public List<Attachment> BuildAttachments { get; set; } = new List<Attachment>();

        public DateTime LastDriverCommunicationUtc { get; set; } = DateTime.UtcNow;

        // dotnet-trace options
        public bool DotNetTrace { get; set; }
        public string DotNetTraceProviders { get; set; }

        // Perfview/Perfcollect
        public bool Collect { get; set; }
        public string CollectArguments { get; set; }
        public string PerfViewTraceFile { get; set; }

        // Other collection options
        public bool CollectStartup { get; set; }
        public bool CollectCounters { get; set; }
        public string BasePath { get; set; }
        public int ProcessId { get; set; }
        public Dictionary<string, string> EnvironmentVariables { get; set; } = new Dictionary<string, string>();
        public List<string> BuildArguments { get; set; } = new List<string>();
        public bool NoClean { get; set; }
        public string Framework { get; set; }
        public string Error { get; set; }
        public string Output { get; set; }
        public bool SelfContained { get; set; }
        public string BeforeScript { get; set; }
        public string AfterScript { get; set; }
        public ulong MemoryLimitInBytes { get; set; }
        public ConcurrentDictionary<string, ConcurrentQueue<string>> Counters { get; set; } = new ConcurrentDictionary<string, ConcurrentQueue<string>>();

        /// <summary>
        /// The build log. This property is kept on the server side.
        /// </summary>
        [JsonIgnore]        
        public string BuildLog { get; set; }
        
        // These properties are used to map custom arguments to the scenario files

        [JsonIgnore]
        public string[] OutputFilesArgument { get; set; } = Array.Empty<string>();

        [JsonProperty("OutputFiles")]
        private string[] OutputFilesArgumentSetter { set { OutputFilesArgument = value; } }

        [JsonIgnore]
        public string[] OutputArchivesArgument { get; set; } = Array.Empty<string>();

        [JsonProperty("OutputArchives")]
        private string[] OutputArchivesArgumentSetter { set { OutputArchivesArgument = value; } }

        [JsonIgnore]
        public string[] BuildFilesArgument { get; set; } = Array.Empty<string>();

        [JsonProperty("BuildFiles")]
        private string[] BuildFilesArgumentSetter { set { BuildFilesArgument = value; } }

        [JsonIgnore]
        public string[] BuildArchivesArgument { get; set; } = Array.Empty<string>();

        [JsonProperty("BuildArchives")]
        private string[] BuildArchivesArgumentSetter { set { BuildArchivesArgument = value; } }
    }
}
