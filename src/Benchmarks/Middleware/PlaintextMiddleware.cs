// Copyright (c) .NET Foundation. All rights reserved. 
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information. 

using System;
using System.Text;
using System.Threading.Tasks;
using Benchmarks.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Benchmarks.Middleware
{
    public class PlaintextMiddleware
    {
        private static readonly PathString _path = new PathString(Scenarios.GetPath(s => s.Plaintext));
        private static readonly byte[] _helloWorldPayload = Encoding.UTF8.GetBytes("Hello, World!");

        private readonly RequestDelegate _next;
        
        public PlaintextMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public Task Invoke(HttpContext httpContext)
        {
            if (httpContext.Request.Path.StartsWithSegments(_path, StringComparison.Ordinal))
            {
                return WriteResponse(httpContext.Response);
            }

            return _next(httpContext);
        }

        public static Task WriteResponse(HttpResponse response)
        {
            response.StatusCode = 200;
            response.ContentType = "text/plain";
            // HACK: Setting the Content-Length header manually avoids the cost of serializing the int to a string.
            //       This is instead of: httpContext.Response.ContentLength = _helloWorldPayload.Length;
            response.Headers["Content-Length"] = "13";
            return response.Body.WriteAsync(_helloWorldPayload, 0, _helloWorldPayload.Length);
        }
    }
    
    public static class PlaintextMiddlewareExtensions
    {
        public static IApplicationBuilder UsePlainText(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<PlaintextMiddleware>();
        }
    }
}
