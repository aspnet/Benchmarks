// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Globalization;
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

        public static Task RenderFortunesHtml(IEnumerable<Fortune> model, HttpContext httpContext, HtmlEncoder htmlEncoder)
        {
            httpContext.Response.StatusCode = StatusCodes.Status200OK;
            httpContext.Response.ContentType = "text/html; charset=UTF-8";

            var sb = StringBuilderCache.Acquire();
            sb.Append("<!DOCTYPE html><html><head><title>Fortunes</title></head><body><table><tr><th>id</th><th>message</th></tr>");

            Span<char> buffer = stackalloc char[256];
            foreach (var item in model)
            {
                sb.Append("<tr><td>");
                sb.Append(CultureInfo.InvariantCulture, $"{item.Id}");
                sb.Append("</td><td>");
                Encode(sb, htmlEncoder, item.Message);
                sb.Append("</td></tr>");
            }

            sb.Append("</table></body></html>");
            var response = StringBuilderCache.GetStringAndRelease(sb);
            // fortunes includes multibyte characters so response.Length is incorrect
            httpContext.Response.ContentLength = Encoding.UTF8.GetByteCount(response);
            return httpContext.Response.WriteAsync(response);

            static void Encode(StringBuilder sb, HtmlEncoder htmlEncoder, string item)
            {
                Span<char> buffer = stackalloc char[256];
                int remaining = item.Length;
                do
                {
                    htmlEncoder.Encode(item.AsSpan()[..remaining], buffer, out var consumed, out var written, isFinalBlock: true);
                    remaining -= consumed;
                    sb.Append(buffer.Slice(0, written));
                } while (remaining != 0);
            }
        }
    }
}
