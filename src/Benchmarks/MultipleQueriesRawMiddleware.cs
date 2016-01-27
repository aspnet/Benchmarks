// Copyright (c) .NET Foundation. All rights reserved. 
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information. 

using System;
using System.Data.Common;
using System.Threading.Tasks;
using Benchmarks.Data;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace Benchmarks
{
    public class MultipleQueriesRawMiddleware
    {
        private static readonly PathString _path = new PathString(Scenarios.GetPaths(s => s.DbMultiQueryRaw)[0]);
        private static readonly JsonSerializerSettings _jsonSettings = new JsonSerializerSettings
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver()
        };

        private readonly RequestDelegate _next;
        private readonly string _connectionString;
        private readonly DbProviderFactory _dbProviderFactory;

        public MultipleQueriesRawMiddleware(RequestDelegate next, string connectionString, DbProviderFactory dbProviderFactory)
        {
            _next = next;
            _connectionString = connectionString;
            _dbProviderFactory = dbProviderFactory;
        }

        public async Task Invoke(HttpContext httpContext)
        {
            if (httpContext.Request.Path.StartsWithSegments(_path, StringComparison.Ordinal))
            {
                var count = MiddlewareHelpers.GetMultipleQueriesQueryCount(httpContext);
                var rows = await RawDb.LoadMultipleQueriesRows(count, _connectionString, _dbProviderFactory);

                var result = JsonConvert.SerializeObject(rows, _jsonSettings);

                httpContext.Response.StatusCode = StatusCodes.Status200OK;
                httpContext.Response.ContentType = "application/json";
                httpContext.Response.ContentLength = result.Length;

                await httpContext.Response.WriteAsync(result);

                return;
            }

            await _next(httpContext);
        }
    }

    public static class MultipleQueriesRawMiddlewareExtensions
    {
        public static IApplicationBuilder UseMultipleQueriesRaw(this IApplicationBuilder builder, string connectionString)
        {
            return builder.UseMiddleware<MultipleQueriesRawMiddleware>(connectionString);
        }
    }
}
