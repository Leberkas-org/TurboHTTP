namespace TurboHTTP.Server;

public sealed class Http3ServerOptions
{
    public int MaxConcurrentStreams { get; set; } = 100;
    public int MaxHeaderListSize { get; set; } = 8192;
    public bool EnableWebTransport { get; set; }
    public long MaxRequestBodySize { get; set; } = 30 * 1024 * 1024;
    public TimeSpan KeepAliveTimeout { get; set; } = TimeSpan.FromSeconds(130);
    public TimeSpan RequestHeadersTimeout { get; set; } = TimeSpan.FromSeconds(30);
    public int MinRequestBodyDataRate { get; set; } = 240;
    public TimeSpan MinRequestBodyDataRateGracePeriod { get; set; } = TimeSpan.FromSeconds(5);
}
