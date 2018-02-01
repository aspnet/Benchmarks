// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Benchmarks.ServerJob;
using Microsoft.AspNetCore.Mvc;
using Repository;

namespace BenchmarkServer.Controllers
{
    [Route("[controller]")]
    public class JobsController : Controller
    {
        private static readonly HttpClient _httpClient = new HttpClient();

        private readonly IRepository<ServerJob> _jobs;

        public JobsController(IRepository<ServerJob> jobs)
        {
            _jobs = jobs;
        }

        public IEnumerable<ServerJob> GetAll()
        {
            lock (_jobs)
            {
                return _jobs.GetAll();
            }
        }

        [HttpGet("{id}/touch")]
        public IActionResult Touch(int id)
        {
            var job = _jobs.Find(id);
            if (job == null)
            {
                return NotFound();
            }
            else
            {
                // Mark when the job was last read to notify that the driver is still connected
                job.LastDriverCommunicationUtc = DateTime.UtcNow;
                return Ok();
            }
        }

        [HttpGet("{id}")]
        public IActionResult GetById(int id)
        {
            lock (_jobs)
            {
                var job = _jobs.Find(id);
                if (job == null)
                {
                    return NotFound();
                }
                else
                {
                    // Mark when the job was last read to notify that the driver is still connected
                    job.LastDriverCommunicationUtc = DateTime.UtcNow;
                    return new ObjectResult(job);
                }
            }   
        }

        [HttpPost]
        public IActionResult Create([FromBody] ServerJob job)
        {
            lock (_jobs)
            {
                if (job == null || job.Id != 0 || job.State != ServerState.Waiting ||
                job.ReferenceSources.Any(source => string.IsNullOrEmpty(source.Repository)))
                {
                    return BadRequest();
                }

                job.Hardware = Startup.Hardware;
                job.HardwareVersion = Startup.HardwareVersion;
                job.OperatingSystem = Startup.OperatingSystem;
                job = _jobs.Add(job);

                Response.Headers["Location"] = $"/jobs/{job.Id}";
                return new StatusCodeResult((int)HttpStatusCode.Accepted);
            }
        }

        [HttpDelete("{id}")]
        public IActionResult Delete(int id)
        {
            lock (_jobs)
            {
                try
                {
                    var job = _jobs.Find(id);
                    job.State = ServerState.Deleting;
                    _jobs.Update(job);

                    Response.Headers["Location"] = $"/jobs/{job.Id}";
                    return new StatusCodeResult((int)HttpStatusCode.Accepted);
                }
                catch
                {
                    return NotFound();
                }
            }
        }

        [HttpPut("{id}")]
        public IActionResult Put([FromBody] ServerJob job)
        {
            try
            {
                var existing = _jobs.Find(job.Id);
                
                if (existing == null)
                {
                    return NotFound();
                }

                _jobs.Update(job);

                return new StatusCodeResult((int)HttpStatusCode.Accepted);
            }
            catch
            {
                return NotFound();
            }
        }

        [HttpPost("{id}/trace")]
        public IActionResult TracePost(int id)
        {
            lock (_jobs)
            {
                try
                {
                    var job = _jobs.Find(id);
                    job.State = ServerState.TraceCollecting;
                    _jobs.Update(job);

                    Response.Headers["Location"] = $"/jobs/{job.Id}";
                    return new StatusCodeResult((int)HttpStatusCode.Accepted);
                }
                catch
                {
                    return NotFound();
                }
            }
        }

        [HttpGet("{id}/trace")]
        public IActionResult Trace(int id)
        {
            lock (_jobs)
            {
                try
                {
                    var job = _jobs.Find(id);
                    return File(System.IO.File.ReadAllBytes(job.PerfViewTraceFile + ".zip"), "application/object");
                }
                catch
                {
                    return NotFound();
                }
            }
        }
        
        [HttpGet("{id}/invoke")]
        public IActionResult Invoke(int id, string path)
        {
            lock (_jobs)
            {
                try
                {
                    var job = _jobs.Find(id);
                    return Content(_httpClient.GetStringAsync(new Uri(new Uri(job.Url), path)).GetAwaiter().GetResult());
                }
                catch (Exception e)
                {
                    return StatusCode(500, e.ToString());
                }
            }
        }
    }
}
