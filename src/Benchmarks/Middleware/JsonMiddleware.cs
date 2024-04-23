// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Benchmarks.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Http.Json;
using System.Text.Json.Serialization.Metadata;

namespace Benchmarks.Middleware
{
    public class JsonMiddleware
    {
        private static readonly PathString _path = new PathString(Scenarios.GetPath(s => s.Json));
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
                httpContext.Response.ContentLength = _bufferSize;

                return httpContext.Response.WriteAsJsonAsync(new JsonMessage { message = "Hello, World!" }, CustomJsonContext.Default.JsonMessage);
            }

            return _next(httpContext);
        }
    }

    public static class JsonMiddlewareExtensions
    {
        public static IApplicationBuilder UseJson(this IApplicationBuilder builder) => builder.UseMiddleware<JsonMiddleware>();
    }

#if NET8_0_OR_GREATER
    [JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
#else
    [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
#endif
    [JsonSerializable(typeof(JsonMessage))]
    internal partial class CustomJsonContext : JsonSerializerContext
    {
    }

    public struct JsonMessage
    {
        public string message { get; set; }
    }
}
