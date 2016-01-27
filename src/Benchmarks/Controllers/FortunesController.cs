// Copyright (c) .NET Foundation. All rights reserved. 
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Data.Common;
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
        private readonly DbProviderFactory _dbProviderFactory;

        public FortunesController(IOptions<Startup.Options> options, DbProviderFactory dbProviderFactory)
        {
            _connectionString = options.Value.ConnectionString;
            _dbProviderFactory = dbProviderFactory;
        }

        [HttpGet("fortunes/raw")]
        public async Task<IActionResult> Raw()
        {
            return View("Fortunes", await RawDb.LoadFortunesRows(_connectionString, _dbProviderFactory));
        }

        [HttpGet("fortunes/dapper")]
        public async Task<IActionResult> Dapper()
        {
            return View("Fortunes", await DapperDb.LoadFortunesRows(_connectionString, _dbProviderFactory));
        }

        [HttpGet("fortunes/ef")]
        public async Task<IActionResult> Ef()
        {
            var dbContext = HttpContext.RequestServices.GetService<ApplicationDbContext>();

            return View("Fortunes", await EfDb.LoadFortunesRows(dbContext));
        }
    }
}
