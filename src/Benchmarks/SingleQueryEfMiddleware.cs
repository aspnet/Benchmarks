// Copyright (c) .NET Foundation. All rights reserved. 
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information. 

using System;
using System.Threading.Tasks;
using Benchmarks.Data;
using Microsoft.AspNet.Builder;
using Microsoft.AspNet.Http;
using Microsoft.Data.Entity;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace Benchmarks
{
    public class SingleQueryEfMiddleware
    {
        private static readonly PathString _path = new PathString("/db/ef");
        private static readonly Random _random = new Random();
        private static readonly JsonSerializerSettings _jsonSettings = new JsonSerializerSettings
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver()
        };
        private readonly RequestDelegate _next;

        public SingleQueryEfMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task Invoke(HttpContext httpContext)
        {
            // We check Ordinal explicitly first because it's faster than OrdinalIgnoreCase
            if (httpContext.Request.Path.StartsWithSegments(_path, StringComparison.Ordinal) ||
                httpContext.Request.Path.StartsWithSegments(_path, StringComparison.OrdinalIgnoreCase))
            {
                var db = (ApplicationDbContext)httpContext.RequestServices.GetService(typeof(ApplicationDbContext));

                db.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;

                var id = _random.Next(1, 10001);
                var row = await db.World.FirstAsync(w => w.Id == id);
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

    public static class SingleQueryEfMiddlewareExtensions
    {
        public static IApplicationBuilder UseSingleQueryEf(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<SingleQueryEfMiddleware>();
        }
    }
}
