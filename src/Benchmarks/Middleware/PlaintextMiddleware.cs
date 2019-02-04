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

#if NETCOREAPP3_0
        public async static Task WriteResponse(HttpResponse response)
        {
            var payloadLength = _helloWorldPayload.Length;
            response.StatusCode = 200;
            response.ContentType = "text/plain";
            response.ContentLength = payloadLength;

            var pipe = response.BodyPipe;
            pipe.Write(_helloWorldPayload);
            await pipe.FlushAsync();
        }
#else
        public static Task WriteResponse(HttpResponse response)
        {
            var payloadLength = _helloWorldPayload.Length;
            response.StatusCode = 200;
            response.ContentType = "text/plain";
            response.ContentLength = payloadLength;
            return response.Body.WriteAsync(_helloWorldPayload, 0, payloadLength);
        }
#endif
    }

    public static class PlaintextMiddlewareExtensions
    {
#if NETCOREAPP3_0
        internal static void Write(this PipeWriter pipe, byte[] payload)
        {
            var span = pipe.GetSpan(sizeHint: payload.Length);
            payload.CopyTo(span);
            pipe.Advance(payload.Length);
        }
#endif

        public static IApplicationBuilder UsePlainText(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<PlaintextMiddleware>();
        }
    }
}
