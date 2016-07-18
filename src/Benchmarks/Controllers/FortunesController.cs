// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Threading.Tasks;
using Benchmarks.Data;
using Microsoft.AspNetCore.Mvc;

namespace Benchmarks.Controllers
{
    public class FortunesController : DbControllerBase
    {
        [HttpGet("fortunes/raw")]
        public async Task<IActionResult> Raw()
        {
            return View("Fortunes", await RawDb.LoadFortunesRows());
        }

        [HttpGet("fortunes/dapper")]
        public async Task<IActionResult> Dapper()
        {
            return View("Fortunes", await DapperDb.LoadFortunesRows());
        }

        [HttpGet("fortunes/ef")]
        public async Task<IActionResult> Ef()
        {
            return View("Fortunes", await EfDb.LoadFortunesRows());
        }
    }
}
