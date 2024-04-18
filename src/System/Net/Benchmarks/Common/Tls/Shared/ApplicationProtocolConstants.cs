// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Net.Security;

namespace System.Net.Benchmarks.Tls;

public static class ApplicationProtocolConstants
{
    // Reuse known ALPN protocols to leverage special casing optimization in SslStream to avoid allocation
    public readonly static SslApplicationProtocol ReadWrite = SslApplicationProtocol.Http11;
    public readonly static SslApplicationProtocol Handshake = SslApplicationProtocol.Http2;
    public readonly static SslApplicationProtocol Rps = SslApplicationProtocol.Http3;
}
