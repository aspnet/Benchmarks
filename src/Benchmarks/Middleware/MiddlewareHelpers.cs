// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Globalization;
using System.IO.Pipelines;
using System.Text;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Benchmarks.Data;
using Microsoft.AspNetCore.Http;

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

        public static async Task RenderFortunesHtml(IEnumerable<Fortune> model, HttpContext httpContext, HtmlEncoder htmlEncoder)
        {
            httpContext.Response.StatusCode = StatusCodes.Status200OK;
            httpContext.Response.ContentType = "text/html; charset=UTF-8";

            Encoding.UTF8.GetBytes("<!DOCTYPE html><html><head><title>Fortunes</title></head><body><table><tr><th>id</th><th>message</th></tr>", httpContext.Response.BodyWriter);

            foreach (var item in model)
            {
                Encoding.UTF8.GetBytes("<tr><td>", httpContext.Response.BodyWriter);
                Encoding.UTF8.GetBytes(item.Id.ToString(CultureInfo.InvariantCulture), httpContext.Response.BodyWriter);
                Encoding.UTF8.GetBytes("</td><td>", httpContext.Response.BodyWriter);
                EncodeToPipe(httpContext.Response.BodyWriter, htmlEncoder, item.Message);
                Encoding.UTF8.GetBytes("</td></tr>", httpContext.Response.BodyWriter);
            }

            Encoding.UTF8.GetBytes("</table></body></html>", httpContext.Response.BodyWriter);

            await httpContext.Response.BodyWriter.FlushAsync();

            static void EncodeToPipe(PipeWriter writer, HtmlEncoder htmlEncoder, string item)
            {
                Span<char> buffer = stackalloc char[256];
                int remaining = item.Length;
                do
                {
                    htmlEncoder.Encode(item.AsSpan()[..remaining], buffer, out var consumed, out var written, isFinalBlock: true);
                    remaining -= consumed;
                    Encoding.UTF8.GetBytes(buffer.Slice(0, written), writer);
                } while (remaining != 0);
            }
        }
    }
}
