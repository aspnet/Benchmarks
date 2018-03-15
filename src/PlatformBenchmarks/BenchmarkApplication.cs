// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Buffers;
using System.IO.Pipelines;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http;

namespace PlatformBenchmarks
{
    public class BenchmarkApplication : HttpConnection
    {
        private static AsciiString _crlf = "\r\n";
        private static AsciiString _eoh = "\r\n\r\n"; // End Of Headers
        private static AsciiString _http11OK = "HTTP/1.1 200 OK\r\n";
        private static AsciiString _headerServer = "Server: Custom";
        private static AsciiString _headerContentLength = "Content-Length: ";
        private static AsciiString _headerContentLengthZero = "Content-Length: 0\r\n";
        private static AsciiString _headerContentTypeText = "Content-Type: text/plain\r\n";

        private static AsciiString _plainTextBody = "Hello, World!";

        private static class Paths
        {
            public static AsciiString Plaintext = "/plaintext";
        }

        private bool _isPlainText;

        public override void OnStartLine(HttpMethod method, HttpVersion version, Span<byte> target, Span<byte> path, Span<byte> query, Span<byte> customMethod, bool pathEncoded)
        {
            _isPlainText = method == HttpMethod.Get && path.StartsWith(Paths.Plaintext);
        }

        public override void OnHeader(Span<byte> name, Span<byte> value)
        {
        }

        public override ValueTask ProcessRequestAsync()
        {
            if (_isPlainText)
            {
                PlainText(Writer);
            }
            else
            {
                Default(Writer);
            }

            return default;
        }

        public override async ValueTask OnReadCompletedAsync()
        {
            await Writer.FlushAsync();
        }

        private static void Default(PipeWriter pipeWriter)
        {
            var writer = new BufferWriter<PipeWriter>(pipeWriter);

            // HTTP 1.1 OK
            writer.Write(_http11OK);

            // Server headers
            writer.Write(_headerServer);

            // Date header
            writer.Write(DateHeader.HeaderBytes);

            // Content-Length 0
            writer.Write(_headerContentLengthZero);

            // End of headers
            writer.Write(_crlf);
            writer.Commit();
        }

        private static void PlainText(PipeWriter pipeWriter)
        {
            var writer = new BufferWriter<PipeWriter>(pipeWriter);
            // HTTP 1.1 OK
            writer.Write(_http11OK);

            // Server headers
            writer.Write(_headerServer);

            // Date header
            writer.Write(DateHeader.HeaderBytes);

            // Content-Type header
            writer.Write(_headerContentTypeText);

            // Content-Length header
            writer.Write(_headerContentLength);
            writer.WriteNumeric((ulong)_plainTextBody.Length);

            // End of headers
            writer.Write(_eoh);

            // Body
            writer.Write(_plainTextBody);
            writer.Commit();
        }
    }
}
