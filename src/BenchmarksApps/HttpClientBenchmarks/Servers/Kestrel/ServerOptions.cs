namespace HttpClientBenchmarks;

public class ServerOptions
{
    public string? Address { get; set; }
    public string? Port { get; set; }
    public bool UseHttps { get; set; }
    public string? HttpVersion { get; set; }
    public int ResponseSize { get; set; }
    public ushort Http3StreamLimit { get; set; }

    public override string ToString()
    {
        return $"Address={Address}; Port={Port}; UseHttps={UseHttps}; HttpVersion={HttpVersion}; ResponseSize={ResponseSize}; Http3StreamLimit={Http3StreamLimit}";
    }
}
