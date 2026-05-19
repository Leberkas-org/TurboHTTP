using TurboHTTP.Protocol.Semantics;

namespace TurboHTTP.Client;

public sealed class CompressionOptions
{
    /// <summary>
    /// The content encoding to apply (e.g. "gzip", "deflate", "br").
    /// Default is "gzip".
    /// </summary>
    public string Encoding { get; set; } = "gzip";

    /// <summary>
    /// Minimum request body size in bytes that triggers compression.
    /// Bodies smaller than this threshold pass through uncompressed.
    /// Default is 1024.
    /// </summary>
    public long MinBodySizeBytes { get; set; } = 1024;

    internal CompressionPolicy To() => new()
    {
        Encoding = Encoding,
        MinBodySizeBytes = MinBodySizeBytes,
    };
}