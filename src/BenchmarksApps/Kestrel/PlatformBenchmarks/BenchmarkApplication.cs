﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO.Pipelines;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http;

namespace PlatformBenchmarks
{
    public partial class BenchmarkApplication
    {
        private readonly static AsciiString _applicationName = "Kestrel Platform-Level Application";
        public static AsciiString ApplicationName => _applicationName;

        private readonly static AsciiString _crlf = "\r\n";
        private readonly static AsciiString _eoh = "\r\n\r\n"; // End Of Headers
        private readonly static AsciiString _http11OK = "HTTP/1.1 200 OK\r\n";
        private readonly static AsciiString _headerServer = "Server: Custom";
        private readonly static AsciiString _headerContentLength = "Content-Length: ";
        private readonly static AsciiString _headerContentLengthZero = "Content-Length: 0\r\n";
        private readonly static AsciiString _headerContentTypeText = "Content-Type: text/plain\r\n";
        private readonly static AsciiString _headerContentTypeJson = "Content-Type: application/json\r\n";

        private readonly static AsciiString _plainTextBody = "Hello, World!";

        private static readonly JsonSerializerOptions SerializerOptions = new JsonSerializerOptions();
        private static readonly JsonMessage _jsonMessage = new JsonMessage { message = "Hello, World!" };
        private static readonly byte[] JsonPayload = JsonSerializer.SerializeToUtf8Bytes(_jsonMessage, SerializerOptions);

        public static class Paths
        {
            public readonly static AsciiString Plaintext = "/plaintext";
            public readonly static AsciiString Json = "/json";
        }

        private RequestType _requestType;

        public void OnStartLine(HttpMethod method, HttpVersion version, Span<byte> target, Span<byte> path, Span<byte> query, Span<byte> customMethod, bool pathEncoded)
        {
            var requestType = RequestType.NotRecognized;
            if (method == HttpMethod.Get)
            {
                if (Paths.Plaintext.Length <= path.Length && path.StartsWith(Paths.Plaintext))
                {
                    requestType = RequestType.PlainText;
                }
                else if (Paths.Json.Length <= path.Length && path.StartsWith(Paths.Json))
                {
                    requestType = RequestType.Json;
                }
            }

            _requestType = requestType;
        }

        public void ProcessRequest()
        {
            if (_requestType == RequestType.PlainText)
            {
                PlainText(Writer);
            }
            else if (_requestType == RequestType.Json)
            {
                Json(Writer);
            }
            else
            {
                Default(Writer);
            }
        }

        private static void PlainText(PipeWriter pipeWriter)
        {
            var writer = GetWriter(pipeWriter, 16 * 132);

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
            writer.WriteNumeric((uint)_plainTextBody.Length);

            // End of headers
            writer.Write(_eoh);

            // Body
            writer.Write(_plainTextBody);
            writer.Commit();
        }

        private static void Json(PipeWriter pipeWriter)
        {
            var writer = GetWriter(pipeWriter, 160);

            // HTTP 1.1 OK
            writer.Write(_http11OK);

            // Server headers
            writer.Write(_headerServer);

            // Date header
            writer.Write(DateHeader.HeaderBytes);

            // Content-Type header
            writer.Write(_headerContentTypeJson);

            // Content-Length header
            writer.Write(_headerContentLength);

            writer.WriteNumeric((uint)JsonPayload.Length);

            // End of headers
            writer.Write(_eoh);
            writer.Commit();

            // Body
            using (var utf8jsonWriter = new Utf8JsonWriter(writer.Output))
            {
                JsonSerializer.Serialize<JsonMessage>(utf8jsonWriter, _jsonMessage, SerializerOptions);
            }
        }

        private static void Default(PipeWriter pipeWriter)
        {
            var writer = GetWriter(pipeWriter, 100);

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

        private enum RequestType
        {
            NotRecognized,
            PlainText,
            Json
        }

        public class JsonMessage
        {
            public string message { get; set; }
        }
    }
}
