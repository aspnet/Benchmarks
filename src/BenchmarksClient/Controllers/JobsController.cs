// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using Benchmarks.ClientJob;
using Microsoft.AspNetCore.Mvc;
using Repository;

namespace BenchmarkClient.Controllers
{
    [Route("[controller]")]
    [ApiController]
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
            if (job == null)
            {
                Log("Invalid job");
                return BadRequest("Invalid job");
            }

            if (job.Id != 0)
            {
                Log("Can't create an existing job");
                return BadRequest("Job.Id must not specified.");
            }

            if (job.State != ClientState.Initializing)
            {
                Log("Job should have ne in Initializing state");
                return BadRequest("Job should have ne in Initializing state");
            }

            job = _jobs.Add(job);
            return AcceptedAtAction(nameof(GetById), new { id = job.Id }, job);
        }

        [HttpPost("{id}/start")]
        public IActionResult Start(int id)
        {
            var job = _jobs.Find(id);
            job.State = ClientState.Waiting;
            _jobs.Update(job);

            Log($"Client job {id} started");
            return Ok();
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
            catch (Exception ex)
            {
                Log(ex.ToString());
                return NotFound();
            }
        }

        [HttpPost("{id}/script")]
        public async Task<IActionResult> UploadScript([FromForm] ScriptViewModel attachment)
        {
            try
            {
                Log($"Receiving script for job '{attachment.Id}'");

                var job = _jobs.Find(attachment.Id);

                if (job == null)
                {
                    return NotFound();
                }

                var tempFilename = Path.GetTempFileName();

                Log($"Creating {attachment.SourceFileName} in {tempFilename}");

                using (var fs = System.IO.File.Create(tempFilename))
                {
                    await attachment.Content.CopyToAsync(fs);
                }

                job.Attachments.Add(new ScriptAttachment
                {
                    TempFilename = tempFilename,
                    Filename = attachment.SourceFileName
                });

                _jobs.Update(job);

                return Ok();
            }
            catch(Exception e)
            {
                Log(e.Message);
                return new StatusCodeResult((int)HttpStatusCode.InternalServerError);
            }
        }

        private static void Log(string message)
        {
            var time = DateTime.Now.ToString("hh:mm:ss.fff");
            Console.WriteLine($"[{time}] {message}");
        }
    }
}
