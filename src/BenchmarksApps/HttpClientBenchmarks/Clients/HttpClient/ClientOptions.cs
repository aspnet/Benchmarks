namespace HttpClientBenchmarks;

public class ClientOptions
{
    public const int DefaultBufferSize = 81920;
    public const int DefaultDuration = 15;

    public string? Address { get; set; }
    public string? Port { get; set; }
    public bool UseHttps { get; set; }
    public string? Path { get; set; }
    public Version? HttpVersion { get; set; }
    public int NumberOfHttpClients { get; set; }
    public int ConcurrencyPerHttpClient { get; set; }
    public int Http11MaxConnectionsPerServer { get; set; }
    public bool Http20EnableMultipleConnections { get; set; }
    public bool UseWinHttpHandler { get; set; }
    public bool UseHttpMessageInvoker { get; set; }
    public bool CollectRequestTimings { get; set; }
    public string? Scenario { get; set; }
    public int ContentSize { get; set; }
    public int ContentWriteSize { get; set; }
    public bool ContentFlushAfterWrite { get; set; }
    public bool ContentUnknownLength { get; set; }
    public List<(string Name, string? Value)> Headers { get; set; } = new();
    public int Warmup { get; set; }
    public int Duration { get; set; }

    public override string ToString()
    {
        return $"Address={Address}; Port={Port}; UseHttps={UseHttps}; Path={Path}; HttpVersion={HttpVersion}; NumberOfHttpClients={NumberOfHttpClients}; " +
            $"ConcurrencyPerHttpClient={ConcurrencyPerHttpClient}; Http11MaxConnectionsPerServer={Http11MaxConnectionsPerServer}; " +
            $"Http20EnableMultipleConnections={Http20EnableMultipleConnections}; UseWinHttpHandler={UseWinHttpHandler}; " +
            $"UseHttpMessageInvoker={UseHttpMessageInvoker}; CollectRequestTimings={CollectRequestTimings}; Scenario={Scenario}; " +
            $"ContentSize={ContentSize}; ContentWriteSize={ContentWriteSize}; ContentFlushAfterWrite={ContentFlushAfterWrite}; " +
            $"ContentUnknownLength={ContentUnknownLength}; Headers=[{string.Join(", ", Headers.Select(h => $"\"{h.Name}: {h.Value}\""))}]; " +
            $"Warmup={Warmup}; Duration={Duration}";
    }
}
