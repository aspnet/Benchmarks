// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

#if NETCOREAPP5_0
using System;

namespace PlatformBenchmarks
{
    public interface IHttpHeadersHandler
    {
        void OnHeader(ReadOnlySpan<byte> name, ReadOnlySpan<byte> value);
        void OnHeadersComplete(bool endStream);
    }
}
#endif
