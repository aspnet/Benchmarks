// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.CommandLine;
using System.CommandLine.Parsing;
using System.Security.Authentication;

namespace System.Net.Security.Benchmarks.SslStream;

public class SslStreamClientOptions : TlsBenchmarkClientOptions, ISslStreamBenchmarkOptions
{
    public bool AllowTlsResume { get; set; }
    public SslProtocols EnabledSslProtocols { get; set; }

    public override string ToString()
        => $"{base.ToString()}, AllowTlsResume: {AllowTlsResume}, EnabledSslProtocols: {EnabledSslProtocols}";
}

public class SslStreamOptionsBinder : TlsBenchmarkClientOptionsBinder<SslStreamClientOptions>
{
    public override void AddCommandLineArguments(RootCommand command)
    {
        base.AddCommandLineArguments(command);
        SslStreamBenchmarkOptionsHelper.AddCommandLineArguments(command);
    }

    protected override void BindOptions(SslStreamClientOptions options, ParseResult parsed)
    {
        base.BindOptions(options, parsed);
        SslStreamBenchmarkOptionsHelper.BindOptions(options, parsed);
    }
}