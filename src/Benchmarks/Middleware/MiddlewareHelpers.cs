// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Benchmarks.Data;
using Microsoft.AspNetCore.Http;
using RazorSlices;

namespace Benchmarks.Middleware
{
    public static class MiddlewareHelpers
    {
        public static int GetMultipleQueriesQueryCount(HttpContext httpContext)
        {
            var queries = 1;
            var queriesRaw = httpContext.Request.Query["queries"];

            if (queriesRaw.Count == 1)
            {
                int.TryParse(queriesRaw, out queries);
            }

            return queries > 500
                ? 500
                : queries > 0
                    ? queries
                    : 1;
        }

        public static async Task RenderFortunesHtml<T>(IEnumerable<T> model, HttpContext httpContext,
            HtmlEncoder htmlEncoder, Func<IEnumerable<T>, RazorSlice> templateFactory)
        {
            httpContext.Response.StatusCode = StatusCodes.Status200OK;
            httpContext.Response.ContentType = "text/html; charset=UTF-8";

            using var template = templateFactory(model);
            await template.RenderAsync(httpContext.Response.BodyWriter, htmlEncoder);
            await httpContext.Response.BodyWriter.FlushAsync();
        }
    }
}
