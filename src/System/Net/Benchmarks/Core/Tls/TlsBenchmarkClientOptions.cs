// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.CommandLine;
using System.Security.Cryptography.X509Certificates;

using System.Net.Benchmarks;
using System.CommandLine.Parsing;

namespace System.Net.Security.Benchmarks;

public enum CertificateSelectionType
{
    CertContext,
    Collection,
    Callback,
}

public class ClientOptions : CommonOptions, IBaseClientOptions
{
    public string Hostname { get; set; } = null!;
    public int Port { get; set; }
    public int Connections { get; set; }
    public int Streams { get; set; }
    public CertificateSelectionType CertificateSelection { get; set; }
    public X509Certificate2? ClientCertificate { get; set; } = null!;
    public string? TlsHostName { get; set; }
    public Scenario Scenario { get; set; }
    public TimeSpan Warmup { get; set; }
    public TimeSpan Duration { get; set; }

    public override string ToString()
        => $"{base.ToString()}, Hostname: {Hostname}, Port: {Port}, Connections: {Connections}, Streams: {Streams}, " +
            $"CertificateSelection: {CertificateSelection}, ClientCertificate: " +
            (ClientCertificate is not null
                ? $"{{ {ClientCertificate.ToString().ReplaceLineEndings(" ")}}}"
                : null) +
            $", TlsHostName: {TlsHostName}, Scenario: {Scenario}, Warmup: {Warmup}, Duration: {Duration}";
}

public class TlsBenchmarkClientOptionsBinder<TOptions> : BenchmarkOptionsBinder<TOptions>
    where TOptions : ClientOptions, new()
{
    public static Option<string> HostOption { get; } = new Option<string>("--host", "The host to connect to.") { IsRequired = true };
    public static Option<int> PortOption { get; } = new Option<int>("--port", () => 9998, "The server port to connect to.");
    public static Option<int> ConnectionsOption { get; } = new Option<int>("--connections", () => 1, "The number of concurrent connections to make.");
    public static Option<int> StreamsOption { get; } = new Option<int>("--streams", () => 1, "The number of streams to open per connection.");
    public static Option<string> ClientCertificatePathOption { get; } = new Option<string>("--cert", "Path to the client certificate.");
    public static Option<string> ClientCertificatePasswordOption { get; } = new Option<string>("--cert-password", "Password to the certificate file specified in --cert.");
    public static Option<CertificateSelectionType> CertificateSelectionOption { get; } = new Option<CertificateSelectionType>("--cert-selection", () => CertificateSelectionType.CertContext, "The source of the server certificate in SslClientAuthenticationOptions.");
    public static Option<string> TlsHostNameOption { get; } = new Option<string>("--tls-host-name", "The target host name to send in TLS Client Hello. If not specified, the value from --host is used.");
    public static Option<Scenario> ScenarioOption { get; } = new Option<Scenario>("--scenario", () => Scenario.ReadWrite, "The scenario to run.");
    public static Option<double> DurationOption { get; } = new Option<double>("--duration", () => 15, "The duration of the test in seconds.");
    public static Option<double> WarmupOption { get; } = new Option<double>("--warmup", () => 15, "The duration of the warmup in seconds.");

    public override void AddCommandLineArguments(RootCommand command)
    {
        command.AddOption(HostOption);
        command.AddOption(PortOption);
        command.AddOption(ScenarioOption);

        OptionsBinderHelper.AddOptions(command);

        command.AddOption(ConnectionsOption);
        command.AddOption(StreamsOption);
        command.AddOption(CertificateSelectionOption);
        command.AddOption(ClientCertificatePathOption);
        command.AddOption(ClientCertificatePasswordOption);
        command.AddOption(TlsHostNameOption);
        command.AddOption(DurationOption);
        command.AddOption(WarmupOption);
    }

    protected override void BindOptions(TOptions options, ParseResult parsed)
    {
        options.Hostname = parsed.GetValueForOption(HostOption)!;
        options.Port = parsed.GetValueForOption(PortOption);
        options.Connections = parsed.GetValueForOption(ConnectionsOption);
        options.Streams = parsed.GetValueForOption(StreamsOption);
        options.ClientCertificate = OptionsBinderHelper.GetCertificateOrNull(
            parsed.GetValueForOption(ClientCertificatePathOption),
            parsed.GetValueForOption(ClientCertificatePasswordOption));
        options.Scenario = parsed.GetValueForOption(ScenarioOption);
        options.CertificateSelection = parsed.GetValueForOption(CertificateSelectionOption);
        options.TlsHostName = parsed.GetValueForOption(TlsHostNameOption);
        options.Duration = TimeSpan.FromSeconds(parsed.GetValueForOption(DurationOption));
        options.Warmup = TimeSpan.FromSeconds(parsed.GetValueForOption(WarmupOption));

        OptionsBinderHelper.BindOptions(options, parsed);
    }
}