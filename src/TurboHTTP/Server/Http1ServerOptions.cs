namespace TurboHTTP.Server;

public sealed class Http1ServerOptions
{
    public int MaxRequestLineLength { get; set; } = 8192;
    public int MaxRequestTargetLength { get; set; } = 8192;
    public int MaxPipelinedRequests { get; set; } = 16;
    public int MaxChunkExtensionLength { get; set; } = 4096;
    public TimeSpan BodyReadTimeout { get; set; } = TimeSpan.FromSeconds(30);
}
