// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.CommandLine;
using System.CommandLine.Parsing;
using System.Security.Authentication;

namespace System.Net.Benchmarks.Tls.SslStream;

public class Options : TlsBenchmarkClientOptions, ISslStreamSpecificOptions
{
    public bool AllowTlsResume { get; set; }
    public SslProtocols EnabledSslProtocols { get; set; }

    public override string ToString()
        => $"{base.ToString()}, AllowTlsResume: {AllowTlsResume}, EnabledSslProtocols: {EnabledSslProtocols}";
}

public class OptionsBinder : TlsBenchmarkClientOptionsBinder<Options>
{
    public override void AddCommandLineArguments(RootCommand command)
    {
        base.AddCommandLineArguments(command);
        OptionsHelper.AddCommandLineArguments(command);
    }

    protected override void BindOptions(Options options, ParseResult parsed)
    {
        base.BindOptions(options, parsed);
        OptionsHelper.BindOptions(options, parsed);
    }
}