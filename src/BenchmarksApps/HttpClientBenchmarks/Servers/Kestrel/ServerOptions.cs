namespace HttpClientBenchmarks;

public class ServerOptions
{
    public string? Url { get; set; }
    public string? HttpVersion { get; set; }

    public override string ToString()
    {
        return $"Url={Url}; HttpVersion={HttpVersion}";
    }
}
