// Copyright (c) .NET Foundation. All rights reserved. 
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Threading.Tasks;
using Benchmarks.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Benchmarks.Controllers
{
    [Route("mvc")]
    public class FortunesController : Controller
    {
        private readonly string _connectionString;

        public FortunesController(IOptions<AppSettings> appSettings)
        {
            _connectionString = appSettings.Value.ConnectionString;
        }

        [HttpGet("fortunes/raw")]
        public async Task<IActionResult> Raw([FromServices] RawDb db)
        {
            return View("Fortunes", await db.LoadFortunesRows(_connectionString));
        }

        [HttpGet("fortunes/dapper")]
        public async Task<IActionResult> Dapper([FromServices] DapperDb db)
        {
            return View("Fortunes", await db.LoadFortunesRows(_connectionString));
        }

        [HttpGet("fortunes/ef")]
        public async Task<IActionResult> Ef([FromServices] EfDb db)
        {
            var dbContext = HttpContext.RequestServices.GetService<ApplicationDbContext>();

            return View("Fortunes", await db.LoadFortunesRows(dbContext));
        }
    }
}
