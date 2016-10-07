// Copyright (c) .NET Foundation. All rights reserved. 
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information. 

using System;
using System.Threading.Tasks;
using Benchmarks.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Benchmarks.Middleware
{
    public class ResponseCachingPlaintextNeverCachedMiddleware
    {
        private static readonly PathString _path = new PathString(Scenarios.GetPath(s => s.ResponseCachingPlaintextNeverCached));

        private readonly RequestDelegate _next;

        public ResponseCachingPlaintextNeverCachedMiddleware(RequestDelegate next)
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

    public static class ResponseCachingPlaintextNeverCachedMiddlewareExtensions
    {
        public static IApplicationBuilder UseResponseCachingPlaintextNeverCached(this IApplicationBuilder builder)
        {
            return builder.UseResponseCache().UseMiddleware<ResponseCachingPlaintextNeverCachedMiddleware>();
        }
    }
}
