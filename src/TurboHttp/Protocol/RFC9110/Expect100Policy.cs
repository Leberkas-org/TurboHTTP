namespace TurboHttp.Protocol.RFC9110;

/// <summary>
/// Configuration for automatic <c>Expect: 100-continue</c> handling.
/// RFC 9110 §10.1.1 — A client that will wait for a 100 (Continue) response before
/// sending the request content MUST send an <c>Expect: 100-continue</c> header field.
/// </summary>
public sealed record Expect100Policy
{
    /// <summary>Default policy: bodies >= 1024 bytes trigger Expect: 100-continue.</summary>
    public static readonly Expect100Policy Default = new();

    /// <summary>
    /// Minimum request body size in bytes that triggers the <c>Expect: 100-continue</c> header.
    /// Requests with a body smaller than this threshold pass through unchanged.
    /// Default is 1024.
    /// </summary>
    public long MinBodySizeBytes { get; init; } = 1024;
}
