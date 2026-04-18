namespace TurboHTTP.Protocol.Http2.Hpack;

/// <summary>
/// Encoding strategy for a single header field.
/// Controls how the encoder serializes a given header.
/// </summary>
internal enum HpackEncoding
{
    /// <summary>
    /// RFC 7541 §6.2.1 – Literal with Incremental Indexing.
    /// The header is added to the dynamic table.
    /// Default strategy for most headers.
    /// </summary>
    IncrementalIndexing,

    /// <summary>
    /// RFC 7541 §6.2.2 – Literal without Indexing.
    /// The header is NOT added to any table.
    /// Useful for one-shot values such as Content-Length or Date.
    /// </summary>
    WithoutIndexing,

    /// <summary>
    /// RFC 7541 §6.2.3 – Literal Never Indexed.
    /// The header MUST NOT be indexed by any intermediary.
    /// Mandatory for security-sensitive fields: Authorization, Cookie, Set-Cookie.
    /// </summary>
    NeverIndexed,
}