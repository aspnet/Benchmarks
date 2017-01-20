﻿// Copyright (c) .NET Foundation. All rights reserved. 
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
    public class MemoryCachePlaintextMiddleware
    {
        private static readonly PathString _path = new PathString(Scenarios.GetPath(s => s.MemoryCachePlaintext));
        private static readonly object _key = new object();

        private readonly RequestDelegate _next;
        private readonly IMemoryCache _memoryCache;

        public MemoryCachePlaintextMiddleware(RequestDelegate next, IMemoryCache memoryCache)
        {
            _next = next;
            _memoryCache = memoryCache;
        }

        public Task Invoke(HttpContext httpContext)
        {
            if (httpContext.Request.Path.StartsWithSegments(_path, StringComparison.Ordinal))
            {
                byte[] payload;
                if (!_memoryCache.TryGetValue(_key, out payload))
                {
                    payload = Encoding.UTF8.GetBytes("Hello, World!");

                    using (var cacheEntry = _memoryCache.CreateEntry(_key))
                    {
                        cacheEntry.SetValue(payload).SetAbsoluteExpiration(TimeSpan.FromSeconds(1));
                    }
                }

                var payloadLength = payload.Length;
                var response = httpContext.Response;
                response.StatusCode = 200;
                response.ContentType = "text/plain";
                response.ContentLength = payloadLength;
                return response.Body.WriteAsync(payload, 0, payloadLength);
            }

            return _next(httpContext);
        }
    }

    public static class MemoryCachePlaintextMiddlewareExtensions
    {
        public static IApplicationBuilder UseMemoryCachePlaintext(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<MemoryCachePlaintextMiddleware>();
        }
    }
}
