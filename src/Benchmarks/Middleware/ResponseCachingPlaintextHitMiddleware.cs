// Copyright (c) .NET Foundation. All rights reserved. 
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information. 

using System;
using System.Threading.Tasks;
using Benchmarks.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Benchmarks.Middleware
{
    public class ResponseCachingPlaintextHitMiddleware
    {
        private static readonly PathString _path = new PathString(Scenarios.GetPath(s => s.ResponseCachingPlaintextHit));

        private readonly RequestDelegate _next;

        public ResponseCachingPlaintextHitMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public Task Invoke(HttpContext httpContext)
        {
            if (httpContext.Request.Path.StartsWithSegments(_path, StringComparison.Ordinal))
            {
                httpContext.Response.Headers["cache-control"] = "public, max-age=1";
                return PlaintextMiddleware.WriteResponse(httpContext.Response);
            }

            return _next(httpContext);
        }
    }

    public static class ResponseCachingPlaintextHitMiddlewareExtensions
    {
        public static IApplicationBuilder UseResponseCachingPlaintextHit(this IApplicationBuilder builder)
        {
            return builder.UseResponseCache().UseMiddleware<ResponseCachingPlaintextHitMiddleware>();
        }
    }
}
