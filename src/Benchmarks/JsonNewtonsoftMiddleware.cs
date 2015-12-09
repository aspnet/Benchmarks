// Copyright (c) .NET Foundation. All rights reserved. 
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information. 

using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNet.Builder;
using Microsoft.AspNet.Http;
using Newtonsoft.Json;

namespace Benchmarks
{
    public class JsonNewtonsoftMiddleware
    {
        private static readonly Task _done = Task.FromResult(0);
        private static readonly PathString _path = new PathString("/json/newtonsoft");
        private static readonly JsonSerializer _json = new JsonSerializer();

        private readonly RequestDelegate _next;
        
        public JsonNewtonsoftMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public Task Invoke(HttpContext httpContext)
        {
            // We check Ordinal explicitly first because it's faster than OrdinalIgnoreCase
            if (httpContext.Request.Path.StartsWithSegments(_path, StringComparison.Ordinal) ||
                httpContext.Request.Path.StartsWithSegments(_path, StringComparison.OrdinalIgnoreCase))
            {
                httpContext.Response.StatusCode = 200;
                httpContext.Response.ContentType = "application/json";
                httpContext.Response.ContentLength = 30;

                using (var sw = new StreamWriter(httpContext.Response.Body, Encoding.UTF8, bufferSize: 30))
                {
                    _json.Serialize(sw, new { message = "Hello, World!" });
                }

                return _done;
            }

            return _next(httpContext);
        }
    }
    
    public static class JsonNewtonsoftMiddlewareExtensions
    {
        public static IApplicationBuilder UseNewtonsoftJson(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<JsonNewtonsoftMiddleware>();
        }
    }
}
