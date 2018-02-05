// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Benchmarks.Configuration;
using Benchmarks.Data;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Benchmarks.Middleware
{
    public class FortunesRawSyncMiddleware
    {
        private static readonly PathString _path = new PathString(Scenarios.GetPath(s => s.DbFortunesRawSync));

        private readonly RequestDelegate _next;
        private readonly HtmlEncoder _htmlEncoder;

        public FortunesRawSyncMiddleware(RequestDelegate next, HtmlEncoder htmlEncoder)
        {
            _next = next;
            _htmlEncoder = htmlEncoder;
        }

        public async Task Invoke(HttpContext httpContext)
        {
            if (httpContext.Request.Path.StartsWithSegments(_path, StringComparison.Ordinal))
            {
                var db = httpContext.RequestServices.GetService<RawDb>();
                var rows = db.LoadFortunesRowsSync();

                await MiddlewareHelpers.RenderFortunesHtml(rows, httpContext, _htmlEncoder);

                return;
            }

            await _next(httpContext);
        }
    }

    public static class FortunesRawMiddlewareSyncExtensions
    {
        public static IApplicationBuilder UseFortunesRawSync(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<FortunesRawSyncMiddleware>();
        }
    }
}
