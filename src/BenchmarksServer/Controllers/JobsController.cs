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
using Repository;

using OperatingSystem = Benchmarks.ServerJob.OperatingSystem;

namespace BenchmarkServer.Controllers
{
    [Route("[controller]")]
    public class JobsController : Controller
    {
        private static readonly Hardware _hardware;
        private static readonly OperatingSystem _operatingSystem;

        private readonly IRepository<ServerJob> _jobs;

        static JobsController()
        {
            var azureLogFile = (string)null;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                _operatingSystem = OperatingSystem.Linux;
                azureLogFile = Path.Combine("var", "log", "waagent.log");
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                _operatingSystem = OperatingSystem.Windows;
                azureLogFile = Path.Combine("%HOMEDRIVE%", "WindowsAzure", "Logs", "WaAppAgent.log");
            }
            else
            {
                throw new InvalidOperationException($"Invalid OSPlatform: {RuntimeInformation.OSDescription}");
            }

            _hardware = System.IO.File.Exists(azureLogFile) ? Hardware.Cloud : Hardware.Physical;
        }

        public JobsController(IRepository<ServerJob> jobs)
        {
            _jobs = jobs;
        }

        public IEnumerable<ServerJob> GetAll()
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
                return new ObjectResult(job);
            }
        }

        [HttpPost]
        public IActionResult Create([FromBody] ServerJob job)
        {
            if (job == null || job.Id != 0 || job.State != ServerState.Waiting ||
                job.Sources.Any(source => string.IsNullOrEmpty(source.Repository)))
            {
                return BadRequest();
            }

            job.Hardware = _hardware;
            job.OperatingSystem = _operatingSystem;
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

    }
}
