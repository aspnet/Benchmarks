// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.CommandLine;
using System.CommandLine.Parsing;
using System.Security.Authentication;

namespace System.Net.Benchmarks.Tls.SslStreamBenchmark;

public interface ISslStreamSpecificOptions
{
    bool AllowTlsResume { get; set; }
    SslProtocols EnabledSslProtocols { get; set; }
}

public static class OptionsHelper
{
    public static Option<Version> TlsVersionOption { get; } = new Option<Version>("--tls-version", () => new Version(1, 3), "The TLS protocol version to use.").FromAmong("1.2", "1.3");
    public static Option<bool> AllowTlsResumeOption { get; } = new Option<bool>("--allow-tls-resume", () => true, "Sets TLS session resumption support.");

#if !NET8_0_OR_GREATER
    static OptionsHelper()
    {
        AllowTlsResumeOption.IsHidden = true;
        AllowTlsResumeOption.AddValidator(symbol =>
        {
            if (!symbol.GetValueOrDefault<bool>())
            {
                return "The option --allow-tls-resume is not supported on this .NET version.";
            }

            return null;
        });
    }
#endif

    public static void AddCommandLineArguments(RootCommand command)
    {
        command.AddOption(TlsVersionOption);
        command.AddOption(AllowTlsResumeOption);
    }

    public static void BindOptions(ISslStreamSpecificOptions options, ParseResult parsed)
    {
        options.AllowTlsResume = parsed.GetValueForOption(AllowTlsResumeOption);
        options.EnabledSslProtocols = parsed.GetValueForOption(TlsVersionOption) switch
        {
            Version { Major: 1, Minor: 2 } => SslProtocols.Tls12,
            Version { Major: 1, Minor: 3 } => SslProtocols.Tls13,
            _ => throw new InvalidOperationException("Invalid TLS version.")
        };
    }
}