using System.CommandLine;
using System.CommandLine.Binding;
using System.Security.Authentication;

using ConnectedStreams.Client;
using SslStreamCommon;

namespace SslStreamClient;
public class SslStreamClientOptions : ClientOptions, ISslOptions
{
    public bool AllowTlsResume { get; set; }
    public SslProtocols EnabledSslProtocols { get; set; }
}

public class SslStreamOptionsBinder : BinderBase<SslStreamClientOptions>
{
    public static void AddOptions(RootCommand command)
    {
        OptionsBinder.AddOptions(command);
        SslOptionsCommon.AddOptions(command);
    }

    protected override SslStreamClientOptions GetBoundValue(BindingContext bindingContext)
    {
        var options = new SslStreamClientOptions();

        OptionsBinder.BindOptions(options, bindingContext);
        SslOptionsCommon.BindOptions(options, bindingContext);

        return options;
    }
}