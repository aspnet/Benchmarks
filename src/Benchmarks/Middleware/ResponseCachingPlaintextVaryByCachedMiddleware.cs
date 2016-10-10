// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Threading.Tasks;
using Benchmarks.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Benchmarks.Middleware
{
    public class ResponseCachingPlaintextVaryByCachedMiddleware
    {
        private static readonly PathString _path = new PathString(Scenarios.GetPath(s => s.ResponseCachingPlaintextVaryByCached));

        private readonly RequestDelegate _next;

        public ResponseCachingPlaintextVaryByCachedMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public Task Invoke(HttpContext httpContext)
        {
            if (httpContext.Request.Path.StartsWithSegments(_path, StringComparison.Ordinal))
            {
                httpContext.Response.Headers["cache-control"] = "public, max-age=1";
                httpContext.Response.Headers["vary"] = "accept";
                return PlaintextMiddleware.WriteResponse(httpContext.Response);
            }

            return _next(httpContext);
        }
    }

    public static class ResponseCachingPlaintextVaryByCachedMiddlewareExtensions
    {
        public static IApplicationBuilder UseResponseCachingPlaintextVaryByCached(this IApplicationBuilder builder)
        {
            return builder.UseResponseCache().UseMiddleware<ResponseCachingPlaintextVaryByCachedMiddleware>();
        }
    }
}
