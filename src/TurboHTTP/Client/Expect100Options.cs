using TurboHTTP.Protocol.Semantics;

namespace TurboHTTP.Client;

public sealed class Expect100Options
{
    /// <summary>
    /// Minimum request body size in bytes that triggers the <c>Expect: 100-continue</c> header.
    /// Requests with a body smaller than this threshold pass through unchanged.
    /// Default is 1024.
    /// </summary>
    public long MinBodySizeBytes { get; set; } = 1024;

    internal Expect100Policy To() => new()
    {
        MinBodySizeBytes = MinBodySizeBytes,
    };
}