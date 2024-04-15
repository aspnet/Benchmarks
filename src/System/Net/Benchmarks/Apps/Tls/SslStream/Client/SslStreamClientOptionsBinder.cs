// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.CommandLine;
using System.CommandLine.Parsing;

namespace System.Net.Benchmarks.Tls.SslStreamBenchmark;

public class SslStreamClientOptionsBinder : TlsBenchmarkClientOptionsBinder<SslStreamClientOptions>
{
    public override void AddCommandLineArguments(RootCommand command)
    {
        base.AddCommandLineArguments(command);
        SslStreamOptionsHelper.AddCommandLineArguments(command);
    }

    protected override void BindOptions(SslStreamClientOptions options, ParseResult parsed)
    {
        base.BindOptions(options, parsed);
        SslStreamOptionsHelper.BindOptions(options, parsed);
    }
}
