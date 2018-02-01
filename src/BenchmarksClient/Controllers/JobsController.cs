// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Net;
using Benchmarks.ClientJob;
using Microsoft.AspNetCore.Mvc;
using Repository;

namespace BenchmarkClient.Controllers
{
    [Route("[controller]")]
    public class JobsController : Controller
    {
        private readonly IRepository<ClientJob> _jobs;

        public JobsController(IRepository<ClientJob> jobs)
        {
            _jobs = jobs;
        }

        public IEnumerable<ClientJob> GetAll()
        {
            return _jobs.GetAll();
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
                job.LastDriverCommunicationUtc = DateTime.UtcNow;
                _jobs.Update(job);
                return new ObjectResult(job);
            }
        }

        [HttpPost]
        public IActionResult Create([FromBody] ClientJob job)
        {
            if (job == null || job.Id != 0 || job.State != ClientState.Waiting)
            {
                return BadRequest();
            }

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
                job.State = ClientState.Deleting;
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
}
