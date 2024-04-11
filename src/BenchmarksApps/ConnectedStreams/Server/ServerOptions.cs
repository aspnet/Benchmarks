using System.CommandLine;
using System.CommandLine.Binding;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

using ConnectedStreams.Shared;

namespace ConnectedStreams.Server;

public enum ServerCertSelectionType
{
    CertContext,
    Certificate,
    Callback,
}

public class ServerOptions : CommonOptions
{
    public ServerCertSelectionType CertificateSelection { get; set; }
    public bool RequireClientCertificate { get; set; }
    public X509Certificate2 ServerCertificate { get; set; } = null!;
    public List<SslApplicationProtocol> ApplicationProtocols { get; set; } = null!;

    public override string ToString()
    {
        return $"{base.ToString()}, CertificateSelection: {CertificateSelection}, RequireClientCertificate: {RequireClientCertificate}, " +
            $"ServerCertificate: {{ {ServerCertificate.ToString().ReplaceLineEndings(" ")}}}";
    }
}

public class OptionsBinder : BinderBase<ServerOptions>
{
    public static Option<int> PortOption { get; } = new Option<int>("--port", () => 9998, "The server port to listen on");
    public static Option<string> ServerCertificatePathOption { get; } = new Option<string>("--cert", "Path to the server certificate. If not specified, a self-signed certificate will be generated.");
    public static Option<string> ServerCertificatePasswordOption { get; } = new Option<string>("--cert-password", "Password to the certificate file specified in --cert.");
    public static Option<ServerCertSelectionType> CertificateSelectionOption { get; } = new Option<ServerCertSelectionType>("--cert-selection", () => ServerCertSelectionType.CertContext, "The source of the server certificate in SslServerAuthenticationOptions.");
    public static Option<string> HostNameOption { get; } = new Option<string>("--host-name", () => "contoso.com", "The host name to use for the generated self-signed certificate.");
    public static Option<bool> RequireClientCertificateOption { get; } = new Option<bool>("--require-client-cert", () => false, "Whether to require a client certificate.");

    public static void AddOptions(RootCommand command)
    {
        command.AddOption(PortOption);

        OptionsBinderHelper.AddOptions(command);

        command.AddOption(CertificateSelectionOption);
        command.AddOption(ServerCertificatePathOption);
        command.AddOption(ServerCertificatePasswordOption);
        command.AddOption(HostNameOption);
        command.AddOption(RequireClientCertificateOption);
    }

    public static void BindOptions(ServerOptions options, BindingContext bindingContext)
    {
        var parsed = bindingContext.ParseResult;

        options.Port = parsed.GetValueForOption(PortOption);
        options.CertificateSelection = parsed.GetValueForOption(CertificateSelectionOption);
        options.RequireClientCertificate = parsed.GetValueForOption(RequireClientCertificateOption);
        options.ServerCertificate = GetCertificate(
            parsed.GetValueForOption(ServerCertificatePathOption),
            parsed.GetValueForOption(ServerCertificatePasswordOption),
            parsed.GetValueForOption(HostNameOption));
        options.ApplicationProtocols = [
            ApplicationProtocolConstants.ReadWrite,
            ApplicationProtocolConstants.Handshake,
            ApplicationProtocolConstants.Rps,
        ];

        OptionsBinderHelper.BindOptions(options, bindingContext);
    }

    protected override ServerOptions GetBoundValue(BindingContext bindingContext)
    {
        var options = new ServerOptions();
        BindOptions(options, bindingContext);
        return options;
    }

    private static X509Certificate2 GetCertificate(string? path, string? password, string? hostname)
    {
        return path is null
            ? OptionsBinderHelper.GenerateSelfSignedCertificate(hostname!, isServer: true)
            : new X509Certificate2(path, password);
    }
}