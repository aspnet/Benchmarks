using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Benchmarks.ServerJob;

namespace BenchmarksServer
{
    public class JobTracker
    {
        public ServerJob job; 

        public Process process = null;

        public string workingDirectory = null;
        public Timer timer = null;
        public object executionLock = new object();
        public bool disposed = false;
        public string benchmarksDir = null;
        public DateTime startMonitorTime = DateTime.UtcNow;

        public string tempDir = null;
        public string dockerImage = null;
        public string dockerContainerId = null;

        public ulong eventPipeSessionId = 0;
        public Task eventPipeTask = null;
        public bool eventPipeTerminated = false;
        
        public ulong measurementsSessionId = 0;
        public Task measurementsTask = null;
        public bool measurementsTerminated = false;
    }
}
