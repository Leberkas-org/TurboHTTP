namespace TurboHttp.Protocol;

/// <summary>
/// HTTP decode error codes based on RFC 9112 (HTTP/1.1 Message Syntax).
/// </summary>
public enum HttpDecodeError
{
    // ── General Errors ──────────────────────────────────────────────────────────

    /// <summary>More data required to complete parsing.</summary>
    NeedMoreData,

    /// <summary>RFC 9112 Section 4: Invalid status-line format.</summary>
    InvalidStatusLine,

    /// <summary>RFC 9112 Section 5: Invalid header field format.</summary>
    InvalidHeader,

    /// <summary>RFC 9112 Section 6.3: Invalid Content-Length value.</summary>
    InvalidContentLength,

    /// <summary>RFC 9112 Section 7.1: Invalid chunked transfer encoding.</summary>
    InvalidChunkedEncoding,

    /// <summary>Content decompression failed.</summary>
    DecompressionFailed,

    // ── RFC 9112 Specific Errors ────────────────────────────────────────────────

    /// <summary>RFC 9112 Section 2.3: Line exceeds configured maximum length.</summary>
    LineTooLong,

    /// <summary>RFC 9112 Section 3: Invalid request-line format.</summary>
    InvalidRequestLine,

    /// <summary>RFC 9112 Section 3.1: Invalid HTTP method token.</summary>
    InvalidMethodToken,

    /// <summary>RFC 9112 Section 3.2: Invalid request target.</summary>
    InvalidRequestTarget,

    /// <summary>RFC 9112 Section 2.3: Invalid HTTP version format.</summary>
    InvalidHttpVersion,

    /// <summary>RFC 9112 Section 5.4: Missing required Host header in HTTP/1.1.</summary>
    MissingHostHeader,

    /// <summary>RFC 9112 Section 5.4: Multiple Host headers present.</summary>
    MultipleHostHeaders,

    /// <summary>RFC 9112 Section 6.3: Multiple Content-Length headers with different values.</summary>
    MultipleContentLengthValues,

    /// <summary>RFC 9112 Section 5.1: Invalid header field name (contains invalid characters).</summary>
    InvalidFieldName,

    /// <summary>RFC 9112 Section 5.5: Invalid header field value.</summary>
    InvalidFieldValue,

    /// <summary>RFC 9112 Section 5.2: Obsolete line folding detected (optional strict mode).</summary>
    ObsoleteFoldingDetected,

    /// <summary>RFC 9112 Section 6.3: Both Transfer-Encoding and Content-Length present.</summary>
    ChunkedWithContentLength,

    /// <summary>RFC 9112 Section 6.5: Invalid trailer header field.</summary>
    InvalidTrailerHeader,

    /// <summary>RFC 9112 Section 7.1.1: Invalid chunk size encoding.</summary>
    InvalidChunkSize,

    /// <summary>RFC 9112 Section 7.1.3: Chunk data truncated.</summary>
    ChunkDataTruncated,

    /// <summary>RFC 9112 Section 7.1.1: Invalid chunk-ext syntax (malformed chunk extension).</summary>
    InvalidChunkExtension,

    /// <summary>Security: Too many header fields in a single message (configurable limit exceeded).</summary>
    TooManyHeaders,
}