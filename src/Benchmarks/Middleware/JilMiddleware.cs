// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Benchmarks.Configuration;
using Benchmarks.Data;
using Jil;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

namespace Benchmarks.Middleware
{
    public class JilMiddleware
    {
        private static readonly PathString _path = new PathString(Scenarios.GetPath(s => s.Jil));
        private static readonly UTF8Encoding _encoding = new UTF8Encoding(false);
        private const int _bufferSize = 27;

        private readonly RequestDelegate _next;

        public JilMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public Task Invoke(HttpContext httpContext)
        {
            if (httpContext.Request.Path.StartsWithSegments(_path, StringComparison.Ordinal))
            {
                httpContext.Response.StatusCode = 200;
                httpContext.Response.ContentType = "application/json";
                httpContext.Response.ContentLength = _bufferSize;

                var syncIOFeature = httpContext.Features.Get<IHttpBodyControlFeature>();
                if (syncIOFeature != null)
                {
                    syncIOFeature.AllowSynchronousIO = true;
                }

                using (var sw = new StreamWriter(httpContext.Response.Body, _encoding, bufferSize: _bufferSize))
                {
                    JSON.Serialize(new JsonMessage() { message = "Hello, World!" }, sw);
                }

                return Task.CompletedTask;
            }

            return _next(httpContext);
        }
    }

    public static class JilMiddlewareExtensions
    {
        public static IApplicationBuilder UseJil(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<JilMiddleware>();
        }
    }
}
