// Copyright (c) .NET Foundation. All rights reserved. 
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information. 

using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNet.Builder;
using Microsoft.AspNet.Http;
using Jil;

namespace Benchmarks
{
    public class JsonJilMiddleware
    {
        private static readonly Task _done = Task.FromResult(0);
        private static readonly PathString _path = new PathString("/json/jil");

        private readonly RequestDelegate _next;
        
        public JsonJilMiddleware(RequestDelegate next)
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
                    JSON.Serialize(new { message = "Hello, World!" }, sw);
                }

                return _done;
            }

            return _next(httpContext);
        }
    }
    
    public static class JsonJilMiddlewareExtensions
    {
        public static IApplicationBuilder UseJilJson(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<JsonJilMiddleware>();
        }
    }
}
