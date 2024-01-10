using System;
using System.CommandLine;
using System.CommandLine.Binding;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using SslStreamCommon;

namespace SslStreamServer;

public enum CertificateSource
{
    Certificate,
    Callback,
    Context
}

public class ServerOptions : OptionsBase
{
    public CertificateSource CertificateSource { get; set; }
    public bool RequireClientCertificate { get; set; }
    public bool DisableTlsResume { get; set; }
    public X509Certificate2 ServerCertificate { get; set; } = null!;
    public List<SslApplicationProtocol> ApplicationProtocols { get; set; } = null!;
}

public class OptionsBinder : BinderBase<ServerOptions>
{
    public static Option<int> PortOption { get; } = new Option<int>("--port", () => 9998, "The server port to listen on");
    public static Option<string> ServerCertificatePathOption { get; } = new Option<string>("--cert", "Path to the server certificate. If not specified, a self-signed certificate will be generated.");
    public static Option<string> ServerCertificatePasswordOption { get; } = new Option<string>("--cert-password", "Password to the certificate file specified in --cert.");
    public static Option<CertificateSource> CertificateSourceOption { get; } = new Option<CertificateSource>("--cert-source", () => CertificateSource.Context, "The source of the server certificate in SslServerAuthenticationOptions.");
    public static Option<string> HostNameOption { get; } = new Option<string>("--host-name", () => "contoso.com", "The host name to use for the generated self-signed certificate.");
    public static Option<bool> RequireClientCertificateOption { get; } = new Option<bool>("--require-client-cert", () => false, "Whether to require a client certificate.");

    public static void AddOptions(RootCommand command)
    {
        command.AddOption(PortOption);

        CommonOptions.AddOptions(command);

        command.AddOption(CertificateSourceOption);
        command.AddOption(ServerCertificatePathOption);
        command.AddOption(ServerCertificatePasswordOption);
        command.AddOption(HostNameOption);
        command.AddOption(RequireClientCertificateOption);
    }

    protected override ServerOptions GetBoundValue(BindingContext bindingContext)
    {
        var parsed = bindingContext.ParseResult;

        var options = new ServerOptions()
        {
            Port = parsed.GetValueForOption(PortOption),
            CertificateSource = parsed.GetValueForOption(CertificateSourceOption),
            RequireClientCertificate = parsed.GetValueForOption(RequireClientCertificateOption),
            ServerCertificate = CommonOptions.GetCertificate(parsed.GetValueForOption(ServerCertificatePathOption), parsed.GetValueForOption(ServerCertificatePasswordOption), parsed.GetValueForOption(HostNameOption))!,
            ApplicationProtocols = Enum.GetValues<Scenario>().Select(v => new SslApplicationProtocol(v.ToString())).ToList(),
        };

        CommonOptions.BindOptions(options, bindingContext);

        return options;
    }
}