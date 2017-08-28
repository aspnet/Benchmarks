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
using Newtonsoft.Json.Linq;

namespace Benchmarks.Middleware
{
    public class JsonMiddleware
    {
        private static readonly PathString _path = new PathString(Scenarios.GetPath(s => s.Json));
        private static readonly UTF8Encoding _encoding = new UTF8Encoding(false);
        private const int _bufferSize = 27;

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
                httpContext.Response.ContentLength = _bufferSize;

                using (var sw = new StreamWriter(httpContext.Response.Body, _encoding, bufferSize: _bufferSize))
                using (var jw = new JsonTextWriter(sw))
                {
                    var obj = new JObject
                    {
                        { "message", new JValue("Hello, World!") }
                    };

                    obj.WriteTo(jw);
                }

                return Task.CompletedTask;
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
