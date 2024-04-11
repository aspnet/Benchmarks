using System.CommandLine;
using System.CommandLine.Binding;
using System.Security.Authentication;

using ConnectedStreams.Server;
using SslStreamCommon;

namespace SslStreamServer;

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

public class SslStreamOptionsBinder : BinderBase<SslStreamServerOptions>
{
    public static void AddOptions(RootCommand command)
    {
        OptionsBinder.AddOptions(command);
        SslOptionsCommon.AddOptions(command);
    }

    protected override SslStreamServerOptions GetBoundValue(BindingContext bindingContext)
    {
        var options = new SslStreamServerOptions();

        OptionsBinder.BindOptions(options, bindingContext);
        SslOptionsCommon.BindOptions(options, bindingContext);

        return options;
    }
}