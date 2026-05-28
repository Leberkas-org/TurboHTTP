namespace TurboHTTP.Server;

public sealed class Http2ServerOptions
{
    public int MaxConcurrentStreams { get; set; } = 100;
    public int InitialConnectionWindowSize { get; set; } = 1 * 1024 * 1024;
    public int InitialStreamWindowSize { get; set; } = 768 * 1024;
    public int MaxFrameSize { get; set; } = 16 * 1024;
    public int HeaderTableSize { get; set; } = 4 * 1024;
    public int MaxHeaderListSize { get; set; } = 32 * 1024;
    public long MaxRequestBodySize { get; set; } = 30_000_000;
    public long MaxResponseBufferSize { get; set; } = 64 * 1024;
    public TimeSpan KeepAliveTimeout { get; set; } = TimeSpan.FromSeconds(130);
    public TimeSpan RequestHeadersTimeout { get; set; } = TimeSpan.FromSeconds(30);
    public int MinRequestBodyDataRate { get; set; } = 240;
    public TimeSpan MinRequestBodyDataRateGracePeriod { get; set; } = TimeSpan.FromSeconds(5);
}