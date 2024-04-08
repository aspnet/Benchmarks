using System.Diagnostics;

namespace SocketBenchmarks.Clients.Basic;

class Program
{
    public static async Task<int> Main(string[] args)
    {
        Utils.Log("Client starting...");
        RootCommand rootCmd = new("Socket client");
        ClientOptionsBinder.AddOptions(rootCmd);
        rootCmd.SetHandler(RunClient, new ClientOptionsBinder());
        return await rootCmd.InvokeAsync(args).ConfigureAwait(false);
    }

    public static async Task RunClient(ClientOptions options)
    {
        switch (options.Scenario)
        {
            case Scenario.ConnectionEstablishment:
                await Scenarios.RunConnectionEstablishment(options);
                break;
            case Scenario.ReadWrite:
                await Scenarios.RunReadWrite(options);
                break;
            default:
                throw new NotSupportedException($"Scenario {options.Scenario} is not supported");
        }
    }
}