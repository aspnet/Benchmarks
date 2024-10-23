// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
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

        public static async Task RenderFortunesHtml(IEnumerable<Fortune> model, HttpContext httpContext, HtmlEncoder htmlEncoder)
        {
            httpContext.Response.StatusCode = StatusCodes.Status200OK;
            httpContext.Response.ContentType = "text/html; charset=UTF-8";

            await httpContext.Response.StartAsync();

            var writer = new BufferWriter<byte>(httpContext.Response.BodyWriter);

            writer.Write("<!DOCTYPE html><html><head><title>Fortunes</title></head><body><table><tr><th>id</th><th>message</th></tr>"u8);

            foreach (var item in model)
            {
                writer.Write("<tr><td>"u8);

                const int maxFormatInt32Length = 10;
                var span = writer.GetSpan(maxFormatInt32Length);
                var res = item.Id.TryFormat(span, out var written, provider: CultureInfo.InvariantCulture);
                Debug.Assert(res);
                writer.Advance(written);

                writer.Write("</td><td>"u8);
                EncodeToPipe(writer, htmlEncoder, item.Message);
                writer.Write("</td></tr>"u8);
            }

            writer.Write("</table></body></html>"u8);

            writer.Commit();

            await httpContext.Response.BodyWriter.FlushAsync();

            static void EncodeToPipe(BufferWriter<byte> writer, HtmlEncoder htmlEncoder, string item)
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

    internal class BufferWriter<T> : IBufferWriter<T>
    {
        private readonly IBufferWriter<T> _inner;
        private Memory<T> _memory;
        private int _buffered;

        public BufferWriter(IBufferWriter<T> writer)
        {
            _inner = writer;
        }

        public void Advance(int count)
        {
            _memory = _memory.Slice(count);
            _buffered += count;
        }

        public void Commit()
        {
            if (_buffered != 0)
            {
                _inner.Advance(_buffered);
                _buffered = 0;
                _memory = default;
            }
        }

        public Memory<T> GetMemory(int sizeHint = 0)
        {
            if (_memory.Length == 0 || _memory.Length < sizeHint)
            {
                Commit();
                _memory = _inner.GetMemory(sizeHint);
            }
            return _memory;
        }

        public Span<T> GetSpan(int sizeHint = 0)
        {
            return GetMemory(sizeHint).Span;
        }
    }
}
