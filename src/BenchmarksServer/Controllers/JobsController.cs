// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
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
                if (job == null || job.Id != 0 || job.State != ServerState.Initializing)
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
                    Log($"Driver deleting job '{id}'");
                    var job = _jobs.Find(id);
                    job.State = ServerState.Deleting;
                    _jobs.Update(job);

                    Response.Headers["Location"] = $"/jobs/{job.Id}";
                    return new StatusCodeResult((int)HttpStatusCode.Accepted);
                }
                catch(Exception e)
                {
                    Log($"Error while deleting job '{id}' " + e.Message);
                    return NotFound();
                }
            }
        }

        [HttpPost("{id}/stop")]
        public IActionResult Stop(int id)
        {
            lock (_jobs)
            {
                Log($"Driver stopping job '{id}'");
                try
                {
                    var job = _jobs.Find(id);
                    job.State = ServerState.Stopping;
                    _jobs.Update(job);

                    Response.Headers["Location"] = $"/jobs/{job.Id}";
                    return new StatusCodeResult((int)HttpStatusCode.Accepted);
                }
                catch(Exception e)
                {
                    Log($"Error while stopping job '{id}' " + e.Message);
                    return NotFound();
                }
            }
        }

        [HttpPost("{id}/resetstats")]
        public IActionResult ResetStats(int id)
        {
            lock (_jobs)
            {
                try
                {
                    var job = _jobs.Find(id);
                    job.ClearServerCounters();
                    _jobs.Update(job);
                    return Ok();
                }
                catch
                {
                    return NotFound();
                }
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

        [HttpPost("{id}/start")]
        public IActionResult Start(int id)
        {
            var job = _jobs.Find(id);
            job.State = ServerState.Waiting;
            _jobs.Update(job);

            return Ok();
        }

        [HttpPost("{id}/attachment")]
        public async Task<IActionResult> UploadAttachment(AttachmentViewModel attachment)
        {
            var job = _jobs.Find(attachment.Id);
            var tempFilename = Path.GetTempFileName();

            using (var fs = System.IO.File.Create(tempFilename))
            {
                await attachment.Content.CopyToAsync(fs);
            }

            job.Attachments.Add(new Attachment
            {
                TempFilename = tempFilename,
                Filename = attachment.DestinationFilename,
            });

            job.LastDriverCommunicationUtc = DateTime.UtcNow;

            return Ok();
        }

        [HttpPost("{id}/source")]
        public async Task<IActionResult> UploadSource(AttachmentViewModel attachment)
        {
            var job = _jobs.Find(attachment.Id);
            var tempFilename = Path.GetTempFileName();

            using (var fs = System.IO.File.Create(tempFilename))
            {
                await attachment.Content.CopyToAsync(fs);
            }

            job.Source.SourceCode = new Attachment
            {
                TempFilename = tempFilename,
                Filename = attachment.DestinationFilename,
            };

            job.LastDriverCommunicationUtc = DateTime.UtcNow;

            return Ok();
        }

        [HttpGet("{id}/trace")]
        public IActionResult Trace(int id)
        {
            lock (_jobs)
            {
                try
                {
                    var job = _jobs.Find(id);
                    return File(System.IO.File.OpenRead(job.PerfViewTraceFile), "application/object");
                }
                catch
                {
                    return NotFound();
                }
            }
        }

        [HttpGet("{id}/fetch")]
        public IActionResult Fetch(int id)
        {
            lock (_jobs)
            {
                try
                {
                    var job = _jobs.Find(id);

                    Log($"Driver fetching published application '{id}'");
                    var zipPath = Path.Combine(Directory.GetParent(job.BasePath).FullName, "published.zip");
                    ZipFile.CreateFromDirectory(job.BasePath, zipPath);

                    return File(System.IO.File.OpenRead(zipPath), "application/object");
                }
                catch
                {
                    return NotFound();
                }
            }
        }

        [HttpGet("{id}/download")]
        public IActionResult Download(int id, string path)
        {
            try
            {
                var job = _jobs.Find(id);

                if (job == null)
                {
                    return NotFound();
                }

                var fullPath = Path.Combine(job.BasePath, path);

                if (!System.IO.File.Exists(fullPath))
                {
                    return NotFound();
                }

                Log($"Uploading {path} ({new FileInfo(fullPath).Length / 1024 + 1} KB)");

                return File(System.IO.File.OpenRead(fullPath), "application/object");
            }
            catch (Exception e)
            {
                return StatusCode(500, e.Message);
            }
        }

        [HttpGet("{id}/invoke")]
        public async Task<IActionResult> Invoke(int id, string path)
        {
            try
            {
                var job = _jobs.Find(id);
                var response = await _httpClient.GetStringAsync(new Uri(new Uri(job.Url), path));
                return Content(response);
            }
            catch (Exception e)
            {
                return StatusCode(500, e.ToString());
            }
        }

        private static void Log(string message)
        {
            var time = DateTime.Now.ToString("hh:mm:ss.fff");
            Console.WriteLine($"[{time}] {message}");
        }

    }
}
