// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Threading.Tasks;
using Benchmarks.Data;
using Microsoft.AspNetCore.Mvc;

namespace Benchmarks.Controllers
{
    public class SingleQueryController : DbControllerBase
    {
        [HttpGet("db/raw")]
        [Produces("application/json")]
        public async Task<object> Raw()
        {
            return await RawDb.LoadSingleQueryRow();
        }

        [HttpGet("db/dapper")]
        [Produces("application/json")]
        public async Task<object> Dapper()
        {
            return await DapperDb.LoadSingleQueryRow();
        }

        [HttpGet("db/ef")]
        [Produces("application/json")]
        public async Task<object> Ef()
        {
            return await EfDb.LoadSingleQueryRow();
        }
    }
}
