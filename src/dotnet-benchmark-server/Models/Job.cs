using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BenchmarkServer.Models
{
    public class Job
    {
        public int Id { get; set; }
        public string Scenario { get; set; }
        public State State { get; set; }
    }
}
