// Copyright (c) .NET Foundation. All rights reserved. 
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information. 

using System;
using System.Threading.Tasks;
using Benchmarks.Data;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace Benchmarks
{
    public class SingleQueryDapperMiddleware
    {
        private static readonly PathString _path = new PathString(Scenarios.GetPaths(s => s.DbSingleQueryDapper)[0]);
        private static readonly JsonSerializerSettings _jsonSettings = new JsonSerializerSettings
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver()
        };

        private readonly RequestDelegate _next;
        private readonly string _connectionString;
        private readonly DapperDb _db;

        public SingleQueryDapperMiddleware(RequestDelegate next, IOptions<AppSettings> appSettings, DapperDb db)
        {
            _next = next;
            _connectionString = appSettings.Value.ConnectionString;
            _db = db;
        }

        public async Task Invoke(HttpContext httpContext)
        {
            if (httpContext.Request.Path.StartsWithSegments(_path, StringComparison.Ordinal))
            {
                var row = await _db.LoadSingleQueryRow(_connectionString);

                var result = JsonConvert.SerializeObject(row, _jsonSettings);

                httpContext.Response.StatusCode = StatusCodes.Status200OK;
                httpContext.Response.ContentType = "application/json";
                httpContext.Response.ContentLength = result.Length;

                await httpContext.Response.WriteAsync(result);

                return;
            }

            await _next(httpContext);
        }
    }

    public static class SingleQueryDapperMiddlewareExtensions
    {
        public static IApplicationBuilder UseSingleQueryDapper(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<SingleQueryDapperMiddleware>();
        }
    }
}
