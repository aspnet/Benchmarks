using System;
using System.CommandLine;
using System.CommandLine.Binding;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using SslStreamCommon;

namespace SslStreamClient;

public enum CertificateSource
{
    Context,
    Certificate,
    Callback,
}

public class ClientOptions : OptionsBase
{
    public string Hostname { get; set; } = null!;
    public CertificateSource CertificateSource { get; set; }
    public X509Certificate2? ClientCertificate { get; set; } = null!;
    public string? TlsHostName { get; set; }
    public Scenario Scenario { get; set; }
    public TimeSpan Warmup { get; set; }
    public TimeSpan Duration { get; set; }
}

public class OptionsBinder : BinderBase<ClientOptions>
{
    public static Option<string> HostOption { get; } = new Option<string>("--host", "The host to connect to.") { IsRequired = true };
    public static Option<int> PortOption { get; } = new Option<int>("--port", () => 9998, "The server port to connect to.");
    public static Option<string> ClientCertificatePathOption { get; } = new Option<string>("--cert", "Path to the client certificate.");
    public static Option<string> ClientCertificatePasswordOption { get; } = new Option<string>("--cert-password", "Password to the certificate file specified in --cert.");
    public static Option<CertificateSource> CertificateSourceOption { get; } = new Option<CertificateSource>("--cert-source", () => CertificateSource.Context, "The source of the server certificate in SslClientAuthenticationOptions.");
    public static Option<string> TlsHostNameOption { get; } = new Option<string>("--tls-host-name", "The target host name to send in TLS Client Hello. If not specified, the value from --host is used.");
    public static Option<Scenario> ScenarioOption { get; } = new Option<Scenario>("--scenario", () => Scenario.ReadWrite, "The scenario to run.");
    public static Option<TimeSpan> DurationOption { get; } = new Option<TimeSpan>("--duration", () => TimeSpan.FromSeconds(15), "The duration of the test.");
    public static Option<TimeSpan> WarmupOption { get; } = new Option<TimeSpan>("--warmup", () => TimeSpan.FromSeconds(15), "The duration of the warmup.");

    public static void AddOptions(RootCommand command)
    {
        command.AddOption(HostOption);
        command.AddOption(PortOption);
        command.AddOption(ScenarioOption);

        CommonOptions.AddOptions(command);

        command.AddOption(CertificateSourceOption);
        command.AddOption(ClientCertificatePathOption);
        command.AddOption(ClientCertificatePasswordOption);
        command.AddOption(TlsHostNameOption);
        command.AddOption(DurationOption);
        command.AddOption(WarmupOption);
    }

    protected override ClientOptions GetBoundValue(BindingContext bindingContext)
    {
        var parsed = bindingContext.ParseResult;

        var options = new ClientOptions()
        {
            Hostname = parsed.GetValueForOption(HostOption)!,
            Port = parsed.GetValueForOption(PortOption),
            ClientCertificate = CommonOptions.GetCertificate(parsed.GetValueForOption(ClientCertificatePathOption), parsed.GetValueForOption(ClientCertificatePasswordOption), null),
            Scenario = parsed.GetValueForOption(ScenarioOption),
            CertificateSource = parsed.GetValueForOption(CertificateSourceOption),
            TlsHostName = parsed.GetValueForOption(TlsHostNameOption),
            Duration = parsed.GetValueForOption(DurationOption),
            Warmup = parsed.GetValueForOption(WarmupOption),
        };

        CommonOptions.BindOptions(options, bindingContext);

        return options;
    }
}