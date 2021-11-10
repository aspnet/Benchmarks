namespace HttpClientBenchmarks;

public class ClientOptions
{
    public string? Url { get; set; }
    public Version? HttpVersion { get; set; }
    public int NumberOfClients { get; set; }
    public int ConcurrencyPerClient { get; set; }
    public int Http11MaxConnectionsPerServer { get; set; }
    public bool Http20EnableMultipleConnections { get; set; }
    public string? Scenario { get; set; }
    public int Warmup { get; set; }
    public int Duration { get; set; }

    public override string ToString()
    {
        return $"Url={Url}; HttpVersion={HttpVersion}; NumberOfClients={NumberOfClients}; ConcurrencyPerClient={ConcurrencyPerClient}; " +
            $"Http11MaxConnectionsPerServer={Http11MaxConnectionsPerServer}; Http20EnableMultipleConnections={Http20EnableMultipleConnections}; " +
            $"Scenario={Scenario}; Warmup={Warmup}; Duration={Duration}";
    }
}
