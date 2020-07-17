// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using Microsoft.AspNetCore.Mvc;

namespace BenchmarkServer.Controllers
{
    [Route("")]
    public class HomeController : Controller
    {
        [HttpGet]
        public IActionResult Index()
        {
            return RedirectToAction("GetQueue", "Jobs");
        }

        [HttpGet("info")]
        public IActionResult Info()
        {
            return Json(new
            {
                hw = Startup.Hardware.ToString(),
                env = Startup.HardwareVersion.ToString(),
                os = Startup.OperatingSystem.ToString(),
                arch = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
                proc = Environment.ProcessorCount
            });
        }
    }
}
