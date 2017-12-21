// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using Benchmarks.ServerJob;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Repository;

namespace BenchmarkServer.Controllers
{
    [Route("[controller]")]
    public class JobsController : Controller
    {
        private readonly IRepository<ServerJob> _jobs;

        public JobsController(IRepository<ServerJob> jobs)
        {
            _jobs = jobs;
        }

        public IEnumerable<ServerJob> GetAll()
        {
            return _jobs.GetAll().Select(PrepareJob);
        }

        [HttpGet("{id}")]
        public IActionResult GetById(int id)
        {
            var job = _jobs.Find(id);
            if (job == null)
            {
                return NotFound();
            }
            else
            {
                return new ObjectResult(PrepareJob(job));
            }
        }

        [HttpPost]
        public IActionResult Create([FromBody] ServerJob job)
        {
            if (job == null || job.Id != 0 || job.State != ServerState.Waiting ||
                job.ReferenceSources.Any(source => string.IsNullOrEmpty(source.Repository)))
            {
                return BadRequest();
            }

            if (job.Attachments != null && job.Attachments.Length > 0)
            {

            }

            job.Hardware = Startup.Hardware;
            job.HardwareVersion = Startup.HardwareVersion;
            job.OperatingSystem = Startup.OperatingSystem;
            job = _jobs.Add(job);

            Response.Headers["Location"] = $"/jobs/{job.Id}";
            return new StatusCodeResult((int)HttpStatusCode.Accepted);
        }

        [HttpDelete("{id}")]
        public IActionResult Delete(int id)
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

        /// <summary>
        /// Creates a cloned job by removing its attachments' content.
        /// </summary>
        private ServerJob PrepareJob(ServerJob job)
        {
            var attachments = job.Attachments;

            job.Attachments = attachments.Select(x => new Attachment { Filename = x.Filename, Location = x.Location }).ToArray();

            var newJob = JsonConvert.DeserializeObject<ServerJob>(JsonConvert.SerializeObject(job));

            job.Attachments = attachments;

            return newJob;
        }
    }
}
