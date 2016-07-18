// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Threading.Tasks;
using Benchmarks.Data;
using Microsoft.AspNetCore.Mvc;

namespace Benchmarks.Controllers
{
    public class MultipleUpdatesController : DbControllerBase
    {
        [HttpGet("updates/raw")]
        [Produces("application/json")]
        public async Task<object> Raw(int queries = 1)
        {
            var count = ValidateQueries(queries);
            return await RawDb.LoadMultipleUpdatesRows(count);
        }

        [HttpGet("updates/dapper")]
        [Produces("application/json")]
        public async Task<object> Dapper(int queries = 1)
        {
            var count = ValidateQueries(queries);
            return await DapperDb.LoadMultipleUpdatesRows(count);
        }

        [HttpGet("updates/ef")]
        [Produces("application/json")]
        public async Task<object> Ef(int queries = 1)
        {
            var count = ValidateQueries(queries);
            return await EfDb.LoadMultipleUpdatesRows(count);
        }
    }
}
