// Copyright (c) .NET Foundation. All rights reserved. 
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information. 

using System;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Benchmarks.Data;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Benchmarks
{
    public class FortunesEfMiddleware
    {
        private static readonly PathString _path = new PathString(Scenarios.GetPaths(s => s.DbFortunesEf)[0]);

        private readonly RequestDelegate _next;
        private readonly HtmlEncoder _htmlEncoder;

        public FortunesEfMiddleware(RequestDelegate next, HtmlEncoder htmlEncoder)
        {
            _next = next;
            _htmlEncoder = htmlEncoder;
        }

        public async Task Invoke(HttpContext httpContext)
        {
            if (httpContext.Request.Path.StartsWithSegments(_path, StringComparison.Ordinal))
            {
                var db = (ApplicationDbContext)httpContext.RequestServices.GetService(typeof(ApplicationDbContext));
                db.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;

                var rows = await EfDb.LoadFortunesRows(db);

                await MiddlewareHelpers.RenderFortunesHtml(rows, httpContext, _htmlEncoder);

                return;
            }

            await _next(httpContext);
        }
    }
    
    public static class FortunesEfMiddlewareExtensions
    {
        public static IApplicationBuilder UseFortunesEf(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<FortunesEfMiddleware>();
        }
    }
}
