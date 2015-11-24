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
    public class MultipleQueriesEfMiddleware
    {
        private static readonly PathString _path = new PathString("/queries/ef");
        private static readonly Random _random = new Random();
        private static readonly JsonSerializerSettings _jsonSettings = new JsonSerializerSettings
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver()
        };
        private readonly RequestDelegate _next;

        public MultipleQueriesEfMiddleware(RequestDelegate next)
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

                var count = GetQueryCount(httpContext);
                var rows = await LoadRows(count, db);

                var result = JsonConvert.SerializeObject(rows, _jsonSettings);

                httpContext.Response.StatusCode = 200;
                httpContext.Response.ContentType = "application/json";
                httpContext.Response.ContentLength = result.Length;

                await httpContext.Response.WriteAsync(result);

                return;
            }

            await _next(httpContext);
        }

        private static int GetQueryCount(HttpContext httpContext)
        {
            var queries = 1;
            var queriesRaw = httpContext.Request.Query["queries"];

            if (queriesRaw.Count == 1)
            {
                int.TryParse(queriesRaw, out queries);
            }

            return queries > 500
                ? 500
                : queries > 0
                    ? queries
                    : 1;
        }

        private static async Task<World[]> LoadRows(int count, ApplicationDbContext dbContext)
        {
            var result = new World[count];

            for (int i = 0; i < count; i++)
            {
                var id = _random.Next(1, 10001);
                result[i] = await dbContext.World.SingleAsync(w => w.Id == id);
            }

            return result;
        }
    }

    public static class MultipleQueriesEfMiddlewareExtensions
    {
        public static IApplicationBuilder UseMultipleQueriesEf(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<MultipleQueriesEfMiddleware>();
        }
    }
}
