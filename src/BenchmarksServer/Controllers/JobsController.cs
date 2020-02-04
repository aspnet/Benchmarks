// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
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

        [HttpGet("info")]
        public IActionResult Info()
        {
            return Json(new
            {
                hw = Startup.Hardware,
                env = Startup.HardwareVersion,
                os = Startup.OperatingSystem,
                arch = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
                proc = Environment.ProcessorCount
            });
        }

        [HttpPost]
        public IActionResult Create([FromBody] ServerJob job)
        {
            lock (_jobs)
            {
                if (job == null || job.Id != 0)
                {
                    return BadRequest();
                }
                else if (job.DriverVersion < 1)
                {
                    return BadRequest("The driver is not compatible with this server, please update it.");
                }
                else if (job.State != ServerState.New)
                {
                    return BadRequest("The job state should be ServerState.New. You are probably using a wrong version of the driver.");
                }

                job.Hardware = Startup.Hardware;
                job.HardwareVersion = Startup.HardwareVersion;
                job.OperatingSystem = Startup.OperatingSystem;
                // Use server-side date and time to prevent issues fron time drifting
                job.LastDriverCommunicationUtc = DateTime.UtcNow;
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

                    if (job == null)
                    {
                        return NoContent();
                    }

                    job.State = ServerState.Deleting;
                    _jobs.Update(job);

                    Response.Headers["Location"] = $"/jobs/{job.Id}";
                    return new StatusCodeResult((int)HttpStatusCode.Accepted);
                }
                catch (Exception e)
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

                    if (job == null)
                    {
                        // The job doesn't exist anymore
                        return NotFound();
                    }

                    if (job.State == ServerState.Stopped || job.State == ServerState.Failed)
                    {
                        // The job might have already been stopped, or deleted.
                        // Can happen if the server stops the job, then the driver does it.
                        // If the benchmark failed, it will be marked as Stopping automatically.
                        return new StatusCodeResult((int)HttpStatusCode.Accepted);
                    }

                    job.State = ServerState.Stopping;
                    _jobs.Update(job);

                    Response.Headers["Location"] = $"/jobs/{job.Id}";
                    return new StatusCodeResult((int)HttpStatusCode.Accepted);
                }
                catch (Exception e)
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
                    job.Counters.Clear();
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
        [RequestSizeLimit(10_000_000_000)]
        [RequestFormLimits(MultipartBodyLengthLimit = 10_000_000_000)]
        public async Task<IActionResult> UploadAttachment(int id)
        {
            var destinationFilename = Request.Headers["destinationFilename"].ToString();

            Log($"Uploading attachment: {destinationFilename}");

            var job = _jobs.Find(id);
            var tempFilename = Path.GetTempFileName();

            using (var fs = System.IO.File.Create(tempFilename))
            {
                await Request.Body.CopyToAsync(fs);
            }

            job.Attachments.Add(new Attachment
            {
                TempFilename = tempFilename,
                Filename = destinationFilename,
            });

            job.LastDriverCommunicationUtc = DateTime.UtcNow;

            return Ok();
        }

        [HttpPost("{id}/source")]
        [RequestSizeLimit(10_000_000_000)]
        [RequestFormLimits(MultipartBodyLengthLimit = 10_000_000_000)]
        public async Task<IActionResult> UploadSource(int id)
        {
            var destinationFilename = Request.Headers["destinationFilename"].ToString();

            Log($"Uploading source code");

            var job = _jobs.Find(id);
            var tempFilename = Path.GetTempFileName();

            using (var fs = System.IO.File.Create(tempFilename))
            {
                await Request.Body.CopyToAsync(fs);
            }

            job.Source.SourceCode = new Attachment
            {
                TempFilename = tempFilename,
                Filename = destinationFilename,
            };

            job.LastDriverCommunicationUtc = DateTime.UtcNow;

            return Ok();
        }

        [HttpPost("{id}/build")]
        [RequestSizeLimit(10_000_000_000)]
        [RequestFormLimits(MultipartBodyLengthLimit = 10_000_000_000)]
        public async Task<IActionResult> UploadBuildFile(int id)
        {
            var destinationFilename = Request.Headers["destinationFilename"].ToString();

            Log($"Uploading build files");

            var job = _jobs.Find(id);
            var tempFilename = Path.GetTempFileName();

            using (var fs = System.IO.File.Create(tempFilename))
            {
                await Request.Body.CopyToAsync(fs);
            }

            job.BuildAttachments.Add(new Attachment
            {
                TempFilename = tempFilename,
                Filename = destinationFilename,
            });

            job.LastDriverCommunicationUtc = DateTime.UtcNow;

            return Ok();
        }

        [HttpGet("{id}/trace")]
        public IActionResult Trace(int id)
        {
            Log($"Downloading trace for job {id}");

            lock (_jobs)
            {
                try
                {
                    var job = _jobs.Find(id);
                    Log($"Sending {job.PerfViewTraceFile}");
                    return File(System.IO.File.OpenRead(job.PerfViewTraceFile), "application/object");
                }
                catch(Exception e)
                {
                    Log("Error: " + e);
                    return NotFound();
                }
            }
        }

        [HttpGet("{id}/buildlog")]
        public IActionResult BuildLog(int id)
        {
            lock (_jobs)
            {
                try
                {
                    var job = _jobs.Find(id);
                    return Content(job.BuildLog.ToString());
                }
                catch
                {
                    return NotFound();
                }
            }
        }

        [HttpGet("{id}/buildlog/{start}")]
        public IActionResult BuildLog(int id, int start)
        {
            lock (_jobs)
            {
                try
                {
                    var job = _jobs.Find(id);

                    return Json(job.BuildLog.Get(start));
                }
                catch
                {
                    return NotFound();
                }
            }
        }

        [HttpGet("{id}/output")]
        public IActionResult Output(int id)
        {
            lock (_jobs)
            {
                try
                {
                    var job = _jobs.Find(id);
                    return Content(job.Output.ToString());
                }
                catch
                {
                    return NotFound();
                }
            }
        }

        [HttpGet("{id}/output/{start}")]
        public IActionResult Output(int id, int start)
        {
            lock (_jobs)
            {
                try
                {
                    var job = _jobs.Find(id);

                    return Json(job.Output.Get(start));
                }
                catch
                {
                    return NotFound();
                }
            }
        }

        [HttpGet("{id}/eventpipe")]
        public IActionResult EventPipe(int id)
        {
            lock (_jobs)
            {
                try
                {
                    var job = _jobs.Find(id);

                    foreach (var file in Directory.GetFiles(job.BasePath, "*.netperf"))
                    {
                        return File(System.IO.File.OpenRead(file), "application/object");
                    }

                    return NotFound();
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

                    if (String.IsNullOrEmpty(job.Source.DockerFile))
                    {
                        var zipPath = Path.Combine(Directory.GetParent(job.BasePath).FullName, "published.zip");
                        ZipFile.CreateFromDirectory(job.BasePath, zipPath);

                        return File(System.IO.File.OpenRead(zipPath), "application/object");
                    }
                    else
                    {
                        var tempDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("n"));
                        Directory.CreateDirectory(tempDirectory);

                        try
                        {
                            // docker cp mycontainer:/src/. target

                            if (String.IsNullOrEmpty(job.Source.DockerFetchPath))
                            {
                                return BadRequest("The Docker fetch path was not provided in the job");
                            }

                            var sourceFolder = Path.Combine(job.Source.DockerFetchPath, ".");

                            // Delete container if the same name already exists
                            ProcessUtil.Run("docker", $"cp {job.Source.GetNormalizedImageName()}:{sourceFolder} {tempDirectory}", throwOnError: false);

                            var zipPath = Path.Combine(Directory.GetParent(job.BasePath).FullName, "fetch.zip");
                            ZipFile.CreateFromDirectory(tempDirectory, zipPath);

                            return File(System.IO.File.OpenRead(zipPath), "application/object");
                        }
                        finally
                        {
                            Response.RegisterForDispose(new TempFolder(tempDirectory));
                        }
                    }
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

                Log($"Download requested: '{fullPath}'");

                if (String.IsNullOrEmpty(job.Source.DockerFile))
                {
                    if (!System.IO.File.Exists(fullPath))
                    {
                        return NotFound();
                    }

                    Log($"Uploading {path} ({new FileInfo(fullPath).Length / 1024 + 1} KB)");

                    return File(System.IO.File.OpenRead(fullPath), "application/object");
                }
                else
                {
                    var tempDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("n"));
                    Directory.CreateDirectory(tempDirectory);

                    try
                    {
                        // docker cp mycontainer:/foo.txt foo.txt 

                        var destinationFilename = Path.Combine(tempDirectory, Path.GetFileName(path));

                        // Delete container if the same name already exists
                        var result = ProcessUtil.Run("docker", $"cp {job.Source.GetNormalizedImageName()}:{path} {destinationFilename}", throwOnError: false, log: true);

                        return File(System.IO.File.OpenRead(destinationFilename), "application/object");
                    }
                    finally
                    {
                        Response.RegisterForDispose(new TempFolder(tempDirectory));
                    }
                }
            }
            catch (Exception e)
            {
                return StatusCode(500, e.Message);
            }
        }

        [HttpGet("{id}/list")]
        public IActionResult List(int id, string path)
        {
            try
            {
                var job = _jobs.Find(id);

                if (job == null)
                {
                    return NotFound();
                }

                var fullPath = Path.Combine(job.BasePath, path);

                if (!Directory.Exists(Path.GetDirectoryName(fullPath)))
                {
                    return Json(Array.Empty<string>());
                }

                if (fullPath.Contains("*"))
                {
                    return Json(
                        Directory.GetFiles(Path.GetDirectoryName(fullPath), Path.GetFileName(fullPath))
                        .Select(x => x.Substring(job.BasePath.Length).TrimStart('/', '\\'))
                        .ToArray()
                        );
                }
                else
                {
                    return Json(
                        Directory.GetFiles(Path.GetDirectoryName(fullPath))
                        .Select(x => x.Substring(job.BasePath.Length).TrimStart('/', '\\'))
                        .ToArray()
                        );
                }
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

        private class TempFolder : IDisposable
        {
            private readonly string _folder;

            public TempFolder(string folder)
            {
                _folder = folder;
            }
            public void Dispose()
            {
                try
                {
                    Directory.Delete(_folder, true);
                }
                catch
                {
                    Log("Could not delete temporary folder: " + _folder);
                }
            }
        }

    }
}
