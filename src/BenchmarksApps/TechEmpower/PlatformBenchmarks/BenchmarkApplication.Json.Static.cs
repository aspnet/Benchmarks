// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Buffers;
using System.Text.Json;

namespace PlatformBenchmarks
{
    public partial class BenchmarkApplication
    {
#if !DATABASE
        private static ReadOnlySpan<byte> _jsonBody => "{\"message\":\"Hello, World!\"}"u8;
        private readonly static uint _jsonPayloadSize = (uint)_payload.Length;

        private readonly static uint _jsonPayloadSize = (uint)_payload.Length;

        private static void JsonStatic(ref BufferWriter<WriterAdapter> writer)
        {
            writer.Write(_jsonPreamble);

            // Date header
            writer.Write(DateHeader.HeaderBytes);

            // Body
            writer.Write(_jsonBody);
        }
#endif
    }
}
