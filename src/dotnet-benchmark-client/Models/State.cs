using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BenchmarkClient.Models
{
    public enum State
    {
        Waiting,
        Starting,
        Running,
        Completed,
        Deleting
    }
}
