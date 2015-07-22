// Copyright (c) .NET Foundation. All rights reserved. 
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information. 

using System;
using System.Threading.Tasks;
using Microsoft.AspNet.Builder;
using Microsoft.AspNet.Http;

namespace Benchmarks
{
    public class DateHeaderMiddleware
    {
        private readonly RequestDelegate _next;

        public DateHeaderMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public Task Invoke(HttpContext httpContext)
        {
            httpContext.Response.Headers.Set("Date", DateTime.UtcNow.ToString("r"));

            return _next(httpContext);
        }
    }

    public static class DateHeaderMiddlewareExtensions
    {
        public static IApplicationBuilder UseDateHeader(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<DateHeaderMiddleware>();
        }
    }
}
