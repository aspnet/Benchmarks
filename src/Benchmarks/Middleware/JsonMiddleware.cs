// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Benchmarks.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

#if NETCOREAPP3_0 || NETCOREAPP3_1 || NETCOREAPP5_0
using System.Text.Json;
using System.Text.Json.Serialization;
#else
using Newtonsoft.Json;
#endif

namespace Benchmarks.Middleware
{
    public class JsonMiddleware
    {
        private static readonly PathString _path = new PathString(Scenarios.GetPath(s => s.Json));
        private static readonly UTF8Encoding _encoding = new UTF8Encoding(false);
        private const int _bufferSize = 27;
#if !NETCOREAPP3_0 && !NETCOREAPP3_1 && !NETCOREAPP5_0
        private static readonly JsonSerializer _json = new JsonSerializer();
#endif
        private readonly RequestDelegate _next;

        public JsonMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task Invoke(HttpContext httpContext)
        {
            if (httpContext.Request.Path.StartsWithSegments(_path, StringComparison.Ordinal))
            {
                httpContext.Response.StatusCode = 200;
                httpContext.Response.ContentType = "application/json";
                httpContext.Response.ContentLength = _bufferSize;

#if !NETCOREAPP3_0 && !NETCOREAPP3_1 && !NETCOREAPP5_0
                var syncIOFeature = httpContext.Features.Get<IHttpBodyControlFeature>();
                if (syncIOFeature != null)
                {
                    syncIOFeature.AllowSynchronousIO = true;
                }

                using (var sw = new StreamWriter(httpContext.Response.Body, _encoding, bufferSize: _bufferSize))
                {
                    _json.Serialize(sw, new JsonMessage { message = "Hello, World!" });
                }
#else
                await JsonSerializer.SerializeAsync<JsonMessage>(httpContext.Response.Body, new JsonMessage { message = "Hello, World!" });
#endif
                return;
            }

            await _next(httpContext);
        }
    }

    public static class JsonMiddlewareExtensions
    {
        public static IApplicationBuilder UseJson(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<JsonMiddleware>();
        }
    }

    public struct JsonMessage
    {
        public string message { get; set; }
    }
}
