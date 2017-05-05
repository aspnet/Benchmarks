// Copyright (c) .NET Foundation. All rights reserved. 
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information. 

using System;
using System.Threading.Tasks;
using Benchmarks.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Benchmarks.Middleware
{
    public class CopyToAsyncMiddleware
    {
        private static readonly PathString _path = new PathString(Scenarios.GetPath(s => s.CopyToAsync));

        private readonly RequestDelegate _next;

        public CopyToAsyncMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public Task Invoke(HttpContext httpContext)
        {
            if (httpContext.Request.Path.StartsWithSegments(_path, StringComparison.Ordinal))
            {
                httpContext.Response.StatusCode = 200;
                httpContext.Response.ContentType = "text/plain";

                return httpContext.Request.Body.CopyToAsync(httpContext.Response.Body);
            }

            return _next(httpContext);
        }
    }

    public static class CopyToAsyncMiddlewareMiddlewareExtensions
    {
        public static IApplicationBuilder UseCopyToAsync(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<CopyToAsyncMiddleware>();
        }
    }
}
