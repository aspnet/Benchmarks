// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Text;
using System.Threading.Tasks;
using Benchmarks.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;

namespace Benchmarks.Middleware
{
    public class MemoryCachePlaintextSetRemoveMiddleware
    {
        private static readonly PathString _path = new PathString(Scenarios.GetPath(s => s.MemoryCachePlaintext));
        private static readonly object _key = new object();
        private static readonly byte[] _helloWorldPayload = Encoding.UTF8.GetBytes("Hello, World!");

        private readonly RequestDelegate _next;
        private readonly IMemoryCache _memoryCache;

        public MemoryCachePlaintextSetRemoveMiddleware(RequestDelegate next, IMemoryCache memoryCache)
        {
            _next = next;
            _memoryCache = memoryCache;
        }

        public Task Invoke(HttpContext httpContext)
        {
            if (httpContext.Request.Path.StartsWithSegments(_path, StringComparison.Ordinal))
            {
                // Not a real scenario. This tests the performance of set and remove operations.
                _memoryCache.Set(_key, _helloWorldPayload);
                _memoryCache.Remove(_key);

                var response = httpContext.Response;
                var payloadLength = _helloWorldPayload.Length;
                response.StatusCode = 200;
                response.ContentType = "text/plain";
                response.ContentLength = payloadLength;
                return response.Body.WriteAsync(_helloWorldPayload, 0, payloadLength);
            }

            return _next(httpContext);
        }
    }

    public static class MemoryCachePlaintextSetRemoveMiddlewareExtensions
    {
        public static IApplicationBuilder UseMemoryCachePlaintextSetRemove(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<MemoryCachePlaintextSetRemoveMiddleware>();
        }
    }
}
