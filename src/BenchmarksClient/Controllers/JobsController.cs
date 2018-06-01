// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;
using Benchmarks.ClientJob;
using BenchmarksClient.Workers;
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

        [HttpPost("{id}/script")]
        public async Task<IActionResult> UploadScript(ScriptViewModel attachment)
        {
            var job = _jobs.Find(attachment.Id);

            var tempFilename = Path.GetTempFileName();

            Log($"Creating {Path.GetFileName(attachment.SourceFileName)} in {tempFilename}");

            using (var fs = System.IO.File.Create(tempFilename))
            {
                await attachment.Content.CopyToAsync(fs);
            }

            job.Attachments.Add(new ScriptAttachment
            {
                TempFilename = tempFilename,
                Filename = attachment.SourceFileName
            });

            using (var fs = System.IO.File.Create(tempFilename))
            {
                await attachment.Content.CopyToAsync(fs);
            }

            return Ok();
        }

        private static void Log(string message)
        {
            var time = DateTime.Now.ToString("hh:mm:ss.fff");
            Console.WriteLine($"[{time}] {message}");
        }
    }
}
