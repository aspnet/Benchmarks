using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BenchmarkClient.Models
{
    public interface IJobRepository
    {
        Job Add(Job job);
        IEnumerable<Job> GetAll();
        Job Find(int id);
        Job Remove(int id);
        void Update(Job job);
    }
}
