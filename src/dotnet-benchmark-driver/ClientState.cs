using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BenchmarkDriver
{
    public enum ClientState
    {
        Waiting,
        Starting,
        Running,
        Completed,
        Deleting
    }
}
