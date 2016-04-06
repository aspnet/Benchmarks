// Copyright (c) .NET Foundation. All rights reserved. 
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information. 

using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Benchmarks.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Newtonsoft.Json;

namespace Benchmarks.Middleware
{
    public class JsonMiddleware
    {
        private const int _contentLength = 27;
        private static readonly Task _done = Task.FromResult(0);
        private static readonly PathString _path = new PathString(Scenarios.GetPath(s => s.Json));
        private static readonly JsonSerializer _json = new JsonSerializer();
        // don't emit a BOM
        private static readonly UTF8Encoding _encoding = new UTF8Encoding(false);

        private readonly RequestDelegate _next;
        
        public JsonMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public Task Invoke(HttpContext httpContext)
        {
            if (httpContext.Request.Path.StartsWithSegments(_path, StringComparison.Ordinal))
            {
                httpContext.Response.StatusCode = 200;
                httpContext.Response.ContentType = "application/json";
                httpContext.Response.ContentLength = _contentLength;

                using (var sw = new StreamWriter(httpContext.Response.Body, _encoding, bufferSize: _contentLength))
                {
                    _json.Serialize(sw, new { message = "Hello, World!" });
                }

                return _done;
            }

            return _next(httpContext);
        }
    }
    
    public static class JsonMiddlewareExtensions
    {
        public static IApplicationBuilder UseJson(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<JsonMiddleware>();
        }
    }
}
