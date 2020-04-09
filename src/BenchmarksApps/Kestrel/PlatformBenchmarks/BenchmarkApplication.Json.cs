// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Text.Json;

namespace PlatformBenchmarks
{
    public partial class BenchmarkApplication
    {
        private readonly static uint _jsonPayloadSize = (uint)JsonSerializer.SerializeToUtf8Bytes(new JsonMessage { message = "Hello, World!" }, SerializerOptions).Length;

        private static void Json(ref BufferWriter<WriterAdapter> writer)
        {
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
            writer.WriteNumeric(_jsonPayloadSize);

            // End of headers
            writer.Write(_eoh);
            writer.Commit();

            // Body
            using (Utf8JsonWriter utf8JsonWriter = new Utf8JsonWriter(writer.Output))
            {
                JsonSerializer.Serialize<JsonMessage>(utf8JsonWriter, new JsonMessage { message = "Hello, World!" }, SerializerOptions);
            }
        }
    }
}
