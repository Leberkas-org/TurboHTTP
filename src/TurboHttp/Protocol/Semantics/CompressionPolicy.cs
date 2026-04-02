namespace TurboHttp.Protocol.Semantics;

/// <summary>
/// Configuration for automatic request body compression.
/// RFC 9110 §8.4 — A sender that applies content encoding MUST generate a Content-Encoding
/// header field listing the applied encodings.
/// </summary>
public sealed record CompressionPolicy
{
    /// <summary>Default policy: gzip encoding, bodies >= 1024 bytes compressed.</summary>
    public static readonly CompressionPolicy Default = new();

    /// <summary>
    /// The content encoding to apply (e.g. "gzip", "deflate", "br").
    /// Default is "gzip".
    /// </summary>
    public string Encoding { get; init; } = "gzip";

    /// <summary>
    /// Minimum request body size in bytes that triggers compression.
    /// Bodies smaller than this threshold pass through uncompressed.
    /// Default is 1024.
    /// </summary>
    public long MinBodySizeBytes { get; init; } = 1024;
}
