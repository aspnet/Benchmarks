namespace HttpClientBenchmarks;

public class ClientOptions
{
    public string? Address { get; set; }
    public string? Port { get; set; }
    public bool UseHttps { get; set; }
    public Version? HttpVersion { get; set; }
    public int NumberOfHttpClients { get; set; }
    public int ConcurrencyPerHttpClient { get; set; }
    public int Http11MaxConnectionsPerServer { get; set; }
    public bool Http20EnableMultipleConnections { get; set; }
    public bool UseWinHttpHandler { get; set; }
    public bool UseHttpMessageInvoker { get; set; }
    public bool CollectRequestTimings { get; set; }
    public string? Scenario { get; set; }
    public int Warmup { get; set; }
    public int Duration { get; set; }

    public override string ToString()
    {
        return $"Address={Address}; Port={Port}; UseHttps={UseHttps}; HttpVersion={HttpVersion}; NumberOfHttpClients={NumberOfHttpClients}; " +
            $"ConcurrencyPerHttpClient={ConcurrencyPerHttpClient}; Http11MaxConnectionsPerServer={Http11MaxConnectionsPerServer}; " +
            $"Http20EnableMultipleConnections={Http20EnableMultipleConnections}; UseWinHttpHandler={UseWinHttpHandler}; " +
            $"UseHttpMessageInvoker={UseHttpMessageInvoker}; CollectRequestTimings={CollectRequestTimings}; Scenario={Scenario}; " +
            $"Warmup={Warmup}; Duration={Duration}";
    }
}
