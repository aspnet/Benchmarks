// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.CommandLine;
using System.CommandLine.Parsing;

namespace System.Net.Benchmarks.Tls;

public class TlsBenchmarkServerOptionsBinder<TOptions> : BenchmarkOptionsBinder<TOptions>
    where TOptions : TlsBenchmarkServerOptions, new()
{
    public static Option<int> PortOption { get; } = new Option<int>("--port", () => 9998, "The server port to listen on");
    public static Option<string> ServerCertificatePathOption { get; } = new Option<string>("--cert", "Path to the server certificate. If not specified, a self-signed certificate will be generated.");
    public static Option<string> ServerCertificatePasswordOption { get; } = new Option<string>("--cert-password", "Password to the certificate file specified in --cert.");
    public static Option<ServerCertSelectionType> CertificateSelectionOption { get; } = new Option<ServerCertSelectionType>("--cert-selection", () => ServerCertSelectionType.CertContext, "The source of the server certificate in SslServerAuthenticationOptions.");
    public static Option<string> HostNameOption { get; } = new Option<string>("--host-name", () => "contoso.com", "The host name to use for the generated self-signed certificate.");
    public static Option<bool> RequireClientCertificateOption { get; } = new Option<bool>("--require-client-cert", () => false, "Whether to require a client certificate.");

    public override void AddCommandLineArguments(RootCommand command)
    {
        command.AddOption(PortOption);

        TlsBenchmarkOptionsHelper.AddOptions(command);

        command.AddOption(CertificateSelectionOption);
        command.AddOption(ServerCertificatePathOption);
        command.AddOption(ServerCertificatePasswordOption);
        command.AddOption(HostNameOption);
        command.AddOption(RequireClientCertificateOption);
    }

    protected override void BindOptions(TOptions options, ParseResult parsed)
    {
        options.Port = parsed.GetValueForOption(PortOption);
        options.CertificateSelection = parsed.GetValueForOption(CertificateSelectionOption);
        options.RequireClientCertificate = parsed.GetValueForOption(RequireClientCertificateOption);
        options.ServerCertificate = TlsBenchmarkOptionsHelper.GetCertificateOrDefault(
            parsed.GetValueForOption(ServerCertificatePathOption),
            parsed.GetValueForOption(ServerCertificatePasswordOption),
            parsed.GetValueForOption(HostNameOption)!);
        options.ApplicationProtocols = [
            ApplicationProtocolConstants.ReadWrite,
            ApplicationProtocolConstants.Handshake,
            ApplicationProtocolConstants.Rps,
        ];

        TlsBenchmarkOptionsHelper.BindOptions(options, parsed);
    }
}
