// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using Repository;

namespace Benchmarks.ServerJob
{
    public class ServerJob : IIdentifiable
    {
        public int DriverVersion { get; set; } = 0;

        // 1: Introduced Initializing state
        // 2: Introduced Measurements/Metadata
        // 3: Output value not serialized
        public int ServerVersion { get; set; } = 3;

        public int Id { get; set; }

        /// <summary>
        /// Common identifier for all jobs in a scenario.
        /// Multiple jobs with the same RunId can be started on the same agent
        /// </summary>
        public string RunId { get; set; } = Guid.NewGuid().ToString("n");

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

        // The client connections. Allows a json document to define this value for the client.
        public int Connections { get; set; }

        // The client threads. Allows a json document to define this value for the client.
        public int Threads { get; set; }
        
        public string ReadyStateText { get; set; }
        
        // A console application doesn't expose and endpoint that can be used to detect it is ready
        public bool IsConsoleApp { get; set; }
        public string AspNetCoreVersion { get; set; } = "";
        public string RuntimeVersion { get; set; } = "";
        public string SdkVersion { get; set; } = "";
        public string UseMonoRuntime { get; set; } = "";
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

        public string Executable { get; set; }
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

        /// <summary>
        /// The list of performance counter providers to be collected. Defaults to <c>System.Runtime</c>.
        /// </summary>
        public List<string> CounterProviders { get; set; } = new List<string>();

        public string BasePath { get; set; }
        public int ProcessId { get; set; }
        public int ChildProcessId { get; set; }
        public Dictionary<string, string> EnvironmentVariables { get; set; } = new Dictionary<string, string>();
        public List<string> BuildArguments { get; set; } = new List<string>();
        public bool NoClean { get; set; }
        public string Framework { get; set; }
        public string Channel { get; set; }
        public string Error { get; set; }
        [JsonIgnore]
        public RollingLog Output { get; set; } = new RollingLog(10000);
        public bool SelfContained { get; set; }
        public string BeforeScript { get; set; }
        public string AfterScript { get; set; }
        public ulong MemoryLimitInBytes { get; set; }
        public double CpuLimitRatio { get; set; }
        public string CpuSet { get; set; } // e.g., 0 or 0-3 or 1-4,6
        public ConcurrentDictionary<string, ConcurrentQueue<string>> Counters { get; set; } = new ConcurrentDictionary<string, ConcurrentQueue<string>>();
        public ConcurrentQueue<Measurement> Measurements { get; set; } = new ConcurrentQueue<Measurement>();
        public ConcurrentQueue<MeasurementMetadata> Metadata { get; set; } = new ConcurrentQueue<MeasurementMetadata>();

        /// <summary>
        /// The build log. This property is kept on the server side.
        /// </summary>
        [JsonIgnore]        
        public RollingLog BuildLog { get; set; } = new RollingLog(10000);

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

        // V2

        public List<string> Endpoints { get; set; } = new List<string>();

        public JObject Variables { get; set; }

        public bool WaitForExit { get; set; }

        public int Timeout { get; set; } = 0;

        // Custom StartTimeout for the server job
        public TimeSpan StartTimeout { get; set; } = TimeSpan.Zero;

        public Options Options { get; set; } = new Options();

        public List<string> Features { get; set; } = new List<string>();

    }

    /// <summary>
    /// Represents a set of properties that configure some behaviors on the driver.
    /// These options are not sent to the server.
    /// </summary>
    public class Options
    {
        public bool DisplayOutput { get; set; }
        public bool Fetch { get; set; }
        public string FetchOutput { get; set; }
        public List<string> DownloadFiles { get; set; } = new List<string>();
        public string TraceOutput { get; set; }
        public bool DisplayBuild { get; set; }
        public string RequiredOperatingSystem { get; set; }
        public string RequiredArchitecture { get; set; }
        public bool DiscardResults { get; set; }
        public List<string> BuildFiles { get; set; } = new List<string>();
        public List<string> OutputFiles { get; set; } = new List<string>();
    }
}
