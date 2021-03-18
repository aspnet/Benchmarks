// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Buffers;
using System.Text.Json;

namespace PlatformBenchmarks
{
    public partial class BenchmarkApplication
    {
        private static bool _initialized = false;
        private static AsciiString _jsonPreamble;

        private static void Json(ref BufferWriter<WriterAdapter> writer, IBufferWriter<byte> bodyWriter)
        {
            // TODO: Use static initialization once https://github.com/dotnet/runtime/issues/49826 is fixed
            if (!_initialized)
            {
                // Not locking, ignoring potential thundering hurd
                _jsonPreamble =
                    _http11OK +
                    _headerServer + _crlf +
                    _headerContentTypeJson + _crlf +
                    _headerContentLength + JsonSerializer.SerializeToUtf8Bytes(new JsonMessage { message = "Hello, World!" }, SerializerOptions).Length.ToString();

                _initialized = true;
            }

            writer.Write(_jsonPreamble);

            // Date header
            writer.Write(DateHeader.HeaderBytes);

            writer.Commit();

            Utf8JsonWriter utf8JsonWriter = t_writer ??= new Utf8JsonWriter(bodyWriter, new JsonWriterOptions { SkipValidation = true });
            utf8JsonWriter.Reset(bodyWriter);

            // Body
            JsonSerializer.Serialize<JsonMessage>(utf8JsonWriter, new JsonMessage { message = "Hello, World!" }, SerializerOptions);
        }
    }
}
