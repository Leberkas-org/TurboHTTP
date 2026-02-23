using System;

namespace TurboHttp.Protocol;

/// <summary>
/// RFC 9110/9112 well-known header names as UTF-8 byte sequences.
/// Enables zero-allocation header comparison during parsing.
/// </summary>
public static class WellKnownHeaders
{
    // ── Request Headers ─────────────────────────────────────────────────────────

    /// <summary>RFC 9110 Section 7.2: Host header (mandatory in HTTP/1.1)</summary>
    public static ReadOnlySpan<byte> Host => "Host"u8;

    /// <summary>RFC 9110 Section 11.6.2: Authorization header</summary>
    public static ReadOnlySpan<byte> Authorization => "Authorization"u8;

    /// <summary>RFC 9110 Section 10.1.1: Accept header</summary>
    public static ReadOnlySpan<byte> Accept => "Accept"u8;

    /// <summary>RFC 9110 Section 12.5.3: Accept-Encoding header</summary>
    public static ReadOnlySpan<byte> AcceptEncoding => "Accept-Encoding"u8;

    /// <summary>RFC 9110 Section 10.1.5: User-Agent header</summary>
    public static ReadOnlySpan<byte> UserAgent => "User-Agent"u8;

    // ── Response Headers ────────────────────────────────────────────────────────

    /// <summary>RFC 9110 Section 10.2.4: Server header</summary>
    public static ReadOnlySpan<byte> Server => "Server"u8;

    /// <summary>RFC 9110 Section 6.6.1: Date header</summary>
    public static ReadOnlySpan<byte> Date => "Date"u8;

    /// <summary>RFC 9110 Section 8.8.3: ETag header</summary>
    public static ReadOnlySpan<byte> ETag => "ETag"u8;

    /// <summary>RFC 9111 Section 5.2: Cache-Control header</summary>
    public static ReadOnlySpan<byte> CacheControl => "Cache-Control"u8;

    // ── Content Headers ─────────────────────────────────────────────────────────

    /// <summary>RFC 9110 Section 8.6: Content-Length header</summary>
    public static ReadOnlySpan<byte> ContentLength => "Content-Length"u8;

    /// <summary>RFC 9110 Section 8.3: Content-Type header</summary>
    public static ReadOnlySpan<byte> ContentType => "Content-Type"u8;

    /// <summary>RFC 9110 Section 8.4: Content-Encoding header</summary>
    public static ReadOnlySpan<byte> ContentEncoding => "Content-Encoding"u8;

    /// <summary>RFC 9112 Section 6.1: Transfer-Encoding header</summary>
    public static ReadOnlySpan<byte> TransferEncoding => "Transfer-Encoding"u8;

    // ── Connection Headers ──────────────────────────────────────────────────────

    /// <summary>RFC 9110 Section 7.6.1: Connection header</summary>
    public static ReadOnlySpan<byte> Connection => "Connection"u8;

    /// <summary>RFC 9112 Section 9.6: Trailer header</summary>
    public static ReadOnlySpan<byte> Trailer => "Trailer"u8;

    // ── Connection Token Values ─────────────────────────────────────────────────

    /// <summary>Connection: keep-alive token</summary>
    public static ReadOnlySpan<byte> KeepAlive => "keep-alive"u8;

    /// <summary>Connection: close token</summary>
    public static ReadOnlySpan<byte> Close => "close"u8;

    /// <summary>Transfer-Encoding: chunked token</summary>
    public static ReadOnlySpan<byte> Chunked => "chunked"u8;

    // ── Protocol Constants ──────────────────────────────────────────────────────

    /// <summary>HTTP/1.1 version string</summary>
    public static ReadOnlySpan<byte> Http11Version => "HTTP/1.1"u8;

    /// <summary>HTTP/1.0 version string</summary>
    public static ReadOnlySpan<byte> Http10Version => "HTTP/1.0"u8;

    /// <summary>CRLF line terminator</summary>
    public static ReadOnlySpan<byte> Crlf => "\r\n"u8;

    /// <summary>Double CRLF (header/body separator)</summary>
    public static ReadOnlySpan<byte> CrlfCrlf => "\r\n\r\n"u8;

    /// <summary>Colon-space separator for header name:value</summary>
    public static ReadOnlySpan<byte> ColonSpace => ": "u8;

    /// <summary>Space character</summary>
    public static ReadOnlySpan<byte> Space => " "u8;

    /// <summary>Comma-space for multi-value headers</summary>
    public static ReadOnlySpan<byte> CommaSpace => ", "u8;

    // ── Comparison Utilities ────────────────────────────────────────────────────

    /// <summary>
    /// Case-insensitive comparison of ASCII header names.
    /// RFC 9110 Section 5.1: Header field names are case-insensitive.
    /// </summary>
    /// <param name="a">First byte sequence</param>
    /// <param name="b">Second byte sequence</param>
    /// <returns>True if sequences are equal ignoring ASCII case</returns>
    public static bool EqualsIgnoreCase(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b)
    {
        if (a.Length != b.Length)
        {
            return false;
        }

        for (var i = 0; i < a.Length; i++)
        {
            // ASCII lowercase: set bit 5 (0x20) to normalize 'A'-'Z' to 'a'-'z'
            // Works for all ASCII letters, preserves non-letters
            if ((a[i] | 0x20) != (b[i] | 0x20))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Checks if a header value contains "chunked" (case-insensitive).
    /// Used for Transfer-Encoding parsing per RFC 9112 Section 6.1.
    /// </summary>
    public static bool ContainsChunked(ReadOnlySpan<byte> value)
    {
        // Simple substring search for "chunked"
        var chunked = Chunked;
        if (value.Length < chunked.Length)
        {
            return false;
        }

        for (var i = 0; i <= value.Length - chunked.Length; i++)
        {
            if (EqualsIgnoreCase(value.Slice(i, chunked.Length), chunked))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Trims leading and trailing ASCII whitespace (SP, HTAB) from a span.
    /// RFC 9110 Section 5.5: OWS = *( SP / HTAB )
    /// </summary>
    public static ReadOnlySpan<byte> TrimOws(ReadOnlySpan<byte> span)
    {
        var start = 0;
        while (start < span.Length && IsOws(span[start]))
        {
            start++;
        }

        var end = span.Length;
        while (end > start && IsOws(span[end - 1]))
        {
            end--;
        }

        return span[start..end];
    }

    /// <summary>
    /// Checks if byte is optional whitespace (SP or HTAB).
    /// RFC 9110 Section 5.6.3: OWS = *( SP / HTAB )
    /// </summary>
    private static bool IsOws(byte b) => b == ' ' || b == '\t';
}
