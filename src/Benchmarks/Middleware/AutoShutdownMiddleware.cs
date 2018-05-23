// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Benchmarks.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;

namespace Benchmarks.Middleware
{
    public class AutoShutdownMiddleware
    {
        private static readonly PathString _path = new PathString("/shutdown");

        private readonly RequestDelegate _next;

        public AutoShutdownMiddleware(RequestDelegate next)
        {
            _next = next;
        }
            
        public async Task Invoke(HttpContext httpContext)
        {
            if (httpContext.Request.Path.StartsWithSegments(_path, StringComparison.Ordinal))
            {
                httpContext.Response.StatusCode = 200;

                var applicationLifeTime = httpContext.RequestServices.GetService<IApplicationLifetime>();
                applicationLifeTime.StopApplication();

                return;
            }
            
            await _next(httpContext);
        }
    }

    public static class AutoShutdownMiddlewareExtensions
    {
        public static IApplicationBuilder UseAutoShutdown(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<AutoShutdownMiddleware>();
        }
    }
}
