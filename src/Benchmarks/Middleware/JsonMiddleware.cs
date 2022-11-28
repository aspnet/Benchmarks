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
using Microsoft.AspNetCore.Http.Features;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Benchmarks.Middleware
{
    public class JsonMiddleware
    {
        private static readonly PathString _path = new PathString(Scenarios.GetPath(s => s.Json));
        private const int _bufferSize = 27;
        private readonly RequestDelegate _next;
        private readonly JsonSerializerOptions _jsonOptions;

        public JsonMiddleware(RequestDelegate next, IOptions<JsonOptions> jsonOptions)
        {
            _next = next;
            _jsonOptions = jsonOptions.Value.SerializerOptions;
        }

        public Task Invoke(HttpContext httpContext)
        {
            if (httpContext.Request.Path.StartsWithSegments(_path, StringComparison.Ordinal))
            {
                httpContext.Response.StatusCode = 200;
                httpContext.Response.ContentLength = _bufferSize;

                return httpContext.Response.WriteAsJsonAsync<JsonMessage>(new JsonMessage { message = "Hello, World!" }, _jsonOptions, httpContext.RequestAborted);
            }

            return _next(httpContext);
        }
    }

    public static class JsonMiddlewareExtensions
    {
        public static IApplicationBuilder UseJson(this IApplicationBuilder builder) => builder.UseMiddleware<JsonMiddleware>();
    }

    [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
    [JsonSerializable(typeof(JsonMessage))]
    internal partial class CustomJsonContext : JsonSerializerContext
    {
    }

    public struct JsonMessage
    {
        public string message { get; set; }
    }
}
