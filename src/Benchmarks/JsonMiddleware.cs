// Copyright (c) .NET Foundation. All rights reserved. 
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information. 

using System;
using System.Buffers;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNet.Builder;
using Microsoft.AspNet.Http;
using Newtonsoft.Json;

namespace Benchmarks
{
    public class JsonMiddleware
    {
        private static readonly Task _done = Task.FromResult(0);
        private static readonly PathString _path = new PathString("/json");
        private static readonly JsonSerializer _json = new JsonSerializer();
        private static readonly JsonArrayPool<char> _arrayPool = new JsonArrayPool<char>(ArrayPool<char>.Shared);

        private readonly RequestDelegate _next;
        
        public JsonMiddleware(RequestDelegate next)
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
                using (var jsonWriter = new JsonTextWriter(sw))
                {
                    jsonWriter.ArrayPool = _arrayPool;

                    _json.Serialize(jsonWriter, new { message = "Hello, World!" });
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

    public class JsonArrayPool<T> : IArrayPool<T>
    {
        private readonly ArrayPool<T> _inner;

        public JsonArrayPool(ArrayPool<T> inner)
        {
            if (inner == null)
            {
                throw new ArgumentNullException(nameof(inner));
            }

            _inner = inner;
        }

        public T[] Rent(int minimumLength)
        {
            return _inner.Rent(minimumLength);
        }

        public void Return(T[] array)
        {
            if (array == null)
            {
                throw new ArgumentNullException(nameof(array));
            }

            _inner.Return(array);
        }
    }
}
