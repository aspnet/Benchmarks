// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Benchmarks.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace Benchmarks.Controllers
{
    [Route("mvc")]
    public abstract class DbControllerBase : Controller
    {
        private RawDb _rawDb;
        private DapperDb _dapperDb;
        private EfDb _efDb;

        protected RawDb RawDb
        {
            get
            {
                if (_rawDb == null)
                {
                    _rawDb = HttpContext.RequestServices.GetRequiredService<RawDb>();
                }

                return _rawDb;
            }
        }

        protected DapperDb DapperDb
        {
            get
            {
                if (_dapperDb == null)
                {
                    _dapperDb = HttpContext.RequestServices.GetRequiredService<DapperDb>();
                }

                return _dapperDb;
            }
        }

        protected EfDb EfDb
        {
            get
            {
                if (_efDb == null)
                {
                    _efDb = HttpContext.RequestServices.GetRequiredService<EfDb>();
                }

                return _efDb;
            }
        }

        protected static int ValidateQueries(int queries)
        {
            if (queries < 1)
            {
                return 1;
            }

            if (queries > 500)
            {
                return 500;
            }

            return queries;
        }
    }
}
