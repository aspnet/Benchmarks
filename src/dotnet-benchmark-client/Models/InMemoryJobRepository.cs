using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using System.Diagnostics;

namespace BenchmarkClient.Models
{
    public class InMemoryJobRepository : IJobRepository
    {
        private readonly object _lock = new object();
        private readonly List<Job> _jobs = new List<Job>();
        private int _nextId = 1;

        public Job Add(Job job)
        {
            if (job.Id != 0)
            {
                throw new ArgumentException("Job.Id must be 0.");
            }

            lock (_lock)
            {
                var id = _nextId;
                _nextId++;
                job.Id = id;
                _jobs.Add(job);
                return job;
            }
        }

        public Job Find(int id)
        {
            lock (_lock)
            {
                var jobs = _jobs.Where(job => job.Id == id);
                Debug.Assert(jobs.Count() == 0 || jobs.Count() == 1);
                return jobs.FirstOrDefault();
            }
        }

        public IEnumerable<Job> GetAll()
        {
            lock (_lock)
            {
                return _jobs.ToArray();
            }
        }

        public Job Remove(int id)
        {
            lock (_lock)
            {
                var job = Find(id);
                if (job == null)
                {
                    throw new ArgumentException($"Could not find Job with Id '{id}'.");
                }
                else
                {
                    _jobs.Remove(job);
                    return job;
                }
            }
        }

        public void Update(Job job)
        {
            lock(_lock)
            {
                var oldJob = Find(job.Id);
                _jobs[_jobs.IndexOf(oldJob)] = job;
            }
        }
    }
}
