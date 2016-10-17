// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Threading.Tasks;
using Benchmarks.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Benchmarks.Middleware
{
    public class ResponseCachingPlaintextResponseNoCacheMiddleware
    {
        private static readonly PathString _path = new PathString(Scenarios.GetPath(s => s.ResponseCachingPlaintextResponseNoCache));

        private readonly RequestDelegate _next;

        public ResponseCachingPlaintextResponseNoCacheMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public Task Invoke(HttpContext httpContext)
        {
            if (httpContext.Request.Path.StartsWithSegments(_path, StringComparison.Ordinal))
            {
                return PlaintextMiddleware.WriteResponse(httpContext.Response);
            }

            return _next(httpContext);
        }
    }

    public static class ResponseCachingPlaintextResponseNoCacheMiddlewareExtensions
    {
        public static IApplicationBuilder UseResponseCachingPlaintextResponseNoCache(this IApplicationBuilder builder)
        {
            return builder.UseResponseCaching().UseMiddleware<ResponseCachingPlaintextResponseNoCacheMiddleware>();
        }
    }
}
