// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.CommandLine;
using System.CommandLine.Binding;
using System.Security.Authentication;

namespace System.Net.Security.Benchmarks.SslStream;

public class SslStreamClientOptions : ClientOptions, ISslOptions
{
    public bool AllowTlsResume { get; set; }
    public SslProtocols EnabledSslProtocols { get; set; }

    public override string ToString()
        => $"{base.ToString()}, AllowTlsResume: {AllowTlsResume}, EnabledSslProtocols: {EnabledSslProtocols}";
}

public class SslStreamOptionsBinder : BinderBase<SslStreamClientOptions>
{
    public static void AddOptions(RootCommand command)
    {
        ClientOptionsBinder.AddOptions(command);
        SslOptionsCommon.AddOptions(command);
    }

    protected override SslStreamClientOptions GetBoundValue(BindingContext bindingContext)
    {
        var options = new SslStreamClientOptions();

        ClientOptionsBinder.BindOptions(options, bindingContext);
        SslOptionsCommon.BindOptions(options, bindingContext);

        return options;
    }
}