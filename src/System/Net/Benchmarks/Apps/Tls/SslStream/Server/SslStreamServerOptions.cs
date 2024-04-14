// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.CommandLine;
using System.CommandLine.Parsing;
using System.Security.Authentication;

namespace System.Net.Security.Benchmarks.SslStream;

public class SslStreamServerOptions : ServerOptions, ISslOptions
{
    public bool DisableTlsResume { get; set; }
    public bool AllowTlsResume { get; set; }
    public SslProtocols EnabledSslProtocols { get; set; }

    public override string ToString()
    {
        return $"{base.ToString()}, DisableTlsResume: {DisableTlsResume}, AllowTlsResume: {AllowTlsResume}, EnabledSslProtocols: {EnabledSslProtocols}";
    }
}

public class SslStreamServerOptionsBinder : TlsBenchmarkServerOptionsBinder<SslStreamServerOptions>
{
    public override void AddCommandLineArguments(RootCommand command)
    {
        base.AddCommandLineArguments(command);
        SslOptionsCommon.AddOptions(command);
    }

    protected override void BindOptions(SslStreamServerOptions options, ParseResult parsed)
    {
        base.BindOptions(options, parsed);
        SslOptionsCommon.BindOptions(options, parsed);
    }
}