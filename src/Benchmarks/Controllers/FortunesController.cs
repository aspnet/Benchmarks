// Copyright (c) .NET Foundation. All rights reserved. 
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Threading.Tasks;
using Benchmarks.Data;
using Microsoft.AspNetCore.Mvc;

namespace Benchmarks.Controllers
{
    [Route("mvc")]
    public class FortunesController : Controller
    {
        private readonly RawDb _rawDb;
        private readonly DapperDb _dapperDb;
        private readonly EfDb _efDb;

        public FortunesController(RawDb rawDb, DapperDb dapperDb, EfDb efDb)
        {
            _rawDb = rawDb;
            _dapperDb = dapperDb;
            _efDb = efDb;
        }

        [HttpGet("fortunes/raw")]
        public async Task<IActionResult> Raw()
        {
            return View("Fortunes", await _rawDb.LoadFortunesRows());
        }

        [HttpGet("fortunes/dapper")]
        public async Task<IActionResult> Dapper()
        {
            return View("Fortunes", await _dapperDb.LoadFortunesRows());
        }

        [HttpGet("fortunes/ef")]
        public async Task<IActionResult> Ef()
        {
            return View("Fortunes", await _efDb.LoadFortunesRows());
        }
    }
}
