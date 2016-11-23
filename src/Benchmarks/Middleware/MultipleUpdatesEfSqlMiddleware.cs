﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Threading.Tasks;
using Benchmarks.Configuration;
using Benchmarks.Data;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace Benchmarks.Middleware
{
    public class MultipleUpdatesEfSqlMiddleware
    {
        private static readonly PathString _path = new PathString(Scenarios.GetPath(s => s.DbMultiUpdateEfSql));
        private static readonly JsonSerializerSettings _jsonSettings = new JsonSerializerSettings
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver()
        };

        private readonly RequestDelegate _next;

        public MultipleUpdatesEfSqlMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task Invoke(HttpContext httpContext)
        {
            if (httpContext.Request.Path.StartsWithSegments(_path, StringComparison.Ordinal))
            {
                var db = httpContext.RequestServices.GetService<EfSqlDb>();
                var count = MiddlewareHelpers.GetMultipleQueriesQueryCount(httpContext);
                var rows = await db.LoadMultipleUpdatesRows(count);

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

    public static class MultipleUpdatesEfSqlMiddlewareExtensions
    {
        public static IApplicationBuilder UseMultipleUpdatesEfSql(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<MultipleUpdatesEfSqlMiddleware>();
        }
    }
}
