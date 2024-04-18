// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Net.Security;

namespace System.Net.Benchmarks.Tls;

internal interface ITlsBenchmarkServerConnection : IConnection
{
    SslApplicationProtocol NegotiatedApplicationProtocol { get; }
    Task CompleteHandshakeAsync(CancellationToken cancellationToken);
    Task<Stream> AcceptInboundStreamAsync(CancellationToken cancellationToken);
}
