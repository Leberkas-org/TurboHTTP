namespace TurboHttp.Protocol;

/// <summary>
/// RFC 9110/9112 well-known header names as UTF-8 byte sequences.
/// Enables zero-allocation header comparison during parsing.
/// </summary>
public static class WellKnownHeaders
{

    /// <summary>RFC 9110 Section 7.2: Host header (mandatory in HTTP/1.1)</summary>
    public static ReadOnlySpan<byte> Host => "Host"u8;

    /// <summary>RFC 9110 Section 11.6.2: Authorization header</summary>
    public static ReadOnlySpan<byte> Authorization => "Authorization"u8;

    /// <summary>RFC 9110 Section 12.5.1: Accept header</summary>
    public static ReadOnlySpan<byte> Accept => "Accept"u8;

    /// <summary>RFC 9110 Section 12.5.3: Accept-Encoding header</summary>
    public static ReadOnlySpan<byte> AcceptEncoding => "Accept-Encoding"u8;

    /// <summary>RFC 9110 Section 10.1.5: User-Agent header</summary>
    public static ReadOnlySpan<byte> UserAgent => "User-Agent"u8;


    /// <summary>RFC 9110 Section 10.2.4: Server header</summary>
    public static ReadOnlySpan<byte> Server => "Server"u8;

    /// <summary>RFC 9110 Section 6.6.1: Date header</summary>
    public static ReadOnlySpan<byte> Date => "Date"u8;

    /// <summary>RFC 9110 Section 8.8.3: ETag header</summary>
    public static ReadOnlySpan<byte> ETag => "ETag"u8;

    /// <summary>RFC 9111 Section 5.2: Cache-Control header</summary>
    public static ReadOnlySpan<byte> CacheControl => "Cache-Control"u8;


    /// <summary>RFC 9110 Section 8.6: Content-Length header</summary>
    public static ReadOnlySpan<byte> ContentLength => "Content-Length"u8;

    /// <summary>RFC 9110 Section 8.3: Content-Type header</summary>
    public static ReadOnlySpan<byte> ContentType => "Content-Type"u8;

    /// <summary>RFC 9110 Section 8.4: Content-Encoding header</summary>
    public static ReadOnlySpan<byte> ContentEncoding => "Content-Encoding"u8;

    /// <summary>RFC 9112 Section 6.1: Transfer-Encoding header</summary>
    public static ReadOnlySpan<byte> TransferEncoding => "Transfer-Encoding"u8;


    /// <summary>RFC 9110 Section 7.6.1: Connection header</summary>
    public static ReadOnlySpan<byte> Connection => "Connection"u8;

    /// <summary>RFC 9110 Section 6.6.2: Trailer header</summary>
    public static ReadOnlySpan<byte> Trailer => "Trailer"u8;


    /// <summary>Connection: keep-alive token</summary>
    public static ReadOnlySpan<byte> KeepAlive => "keep-alive"u8;

    /// <summary>Connection: close token</summary>
    public static ReadOnlySpan<byte> Close => "close"u8;

    /// <summary>Transfer-Encoding: chunked token</summary>
    public static ReadOnlySpan<byte> Chunked => "chunked"u8;


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

    // For use with System.Net.Http APIs that compare header names as strings.

    /// <summary>Header name strings for use with System.Net.Http APIs.</summary>
#pragma warning disable CS0108 // Nested constants intentionally shadow outer byte-span properties
    public static class Names
    {
        public const string Host = "Host";
        public const string Connection = "Connection";
        public const string ContentLength = "Content-Length";
        public const string ContentEncoding = "Content-Encoding";
        public const string TransferEncoding = "Transfer-Encoding";
    }
#pragma warning restore CS0108


    /// <summary>RFC 9110 §8.4.1: identity encoding (no transformation)</summary>
    public const string Identity = "identity";

    /// <summary>RFC 9110 §8.4.1.3: gzip encoding</summary>
    public const string Gzip = "gzip";

    /// <summary>Legacy alias for gzip</summary>
    public const string XGzip = "x-gzip";

    /// <summary>RFC 9110 §8.4.1.2: deflate encoding</summary>
    public const string Deflate = "deflate";

    /// <summary>RFC 7932: Brotli encoding</summary>
    public const string Brotli = "br";


    /// <summary>
    /// Returns the interned string for a well-known HTTP header name, or allocates
    /// a new string for unknown names. Avoids <see cref="System.Text.Encoding.ASCII"/>
    /// allocations for the ~25 most common response headers.
    /// </summary>
    /// <remarks>
    /// Uses length as the first discriminator (O(1)) then a byte-sequence comparison
    /// for candidates at that length — same technique as the .NET runtime's HttpConnection.
    /// </remarks>
    public static string GetOrCreateHeaderName(ReadOnlySpan<byte> name)
        => name.Length switch
        {
            2 => name.SequenceEqual("TE"u8)                    ? "TE"                    : System.Text.Encoding.ASCII.GetString(name),
            3 => name.SequenceEqual("Age"u8)                   ? "Age"                   :
                 name.SequenceEqual("Via"u8)                   ? "Via"                   : System.Text.Encoding.ASCII.GetString(name),
            4 => name.SequenceEqual("Date"u8)                  ? "Date"                  :
                 name.SequenceEqual("ETag"u8)                  ? "ETag"                  :
                 name.SequenceEqual("Vary"u8)                  ? "Vary"                  :
                 name.SequenceEqual("From"u8)                  ? "From"                  :
                 name.SequenceEqual("Host"u8)                  ? "Host"                  :
                 name.SequenceEqual("Link"u8)                  ? "Link"                  : System.Text.Encoding.ASCII.GetString(name),
            5 => name.SequenceEqual("Allow"u8)                 ? "Allow"                 :
                 name.SequenceEqual("Retry"u8)                 ? "Retry"                 : System.Text.Encoding.ASCII.GetString(name),
            6 => name.SequenceEqual("Accept"u8)                ? "Accept"                :
                 name.SequenceEqual("Cookie"u8)                ? "Cookie"                :
                 name.SequenceEqual("Expect"u8)                ? "Expect"                :
                 name.SequenceEqual("Pragma"u8)                ? "Pragma"                :
                 name.SequenceEqual("Server"u8)                ? "Server"                : System.Text.Encoding.ASCII.GetString(name),
            7 => name.SequenceEqual("Alt-Svc"u8)               ? "Alt-Svc"               :
                 name.SequenceEqual("Expires"u8)               ? "Expires"               :
                 name.SequenceEqual("Referer"u8)               ? "Referer"               :
                 name.SequenceEqual("Trailer"u8)               ? "Trailer"               :
                 name.SequenceEqual("Upgrade"u8)               ? "Upgrade"               :
                 name.SequenceEqual("Warning"u8)               ? "Warning"               : System.Text.Encoding.ASCII.GetString(name),
            8 => name.SequenceEqual("If-Match"u8)              ? "If-Match"              :
                 name.SequenceEqual("If-Range"u8)              ? "If-Range"              :
                 name.SequenceEqual("Location"u8)              ? "Location"              : System.Text.Encoding.ASCII.GetString(name),
            10 => name.SequenceEqual("Connection"u8)           ? "Connection"            :
                  name.SequenceEqual("Set-Cookie"u8)           ? "Set-Cookie"            :
                  name.SequenceEqual("User-Agent"u8)           ? "User-Agent"            : System.Text.Encoding.ASCII.GetString(name),
            11 => name.SequenceEqual("Retry-After"u8)          ? "Retry-After"           :
                  name.SequenceEqual("Set-Cookie2"u8)          ? "Set-Cookie2"           : System.Text.Encoding.ASCII.GetString(name),
            12 => name.SequenceEqual("Content-Type"u8)         ? "Content-Type"          :
                  name.SequenceEqual("Last-Modified"u8)        ? "Last-Modified"         :
                  name.SequenceEqual("Max-Forwards"u8)         ? "Max-Forwards"          : System.Text.Encoding.ASCII.GetString(name),
            13 => name.SequenceEqual("Authorization"u8)        ? "Authorization"         :
                  name.SequenceEqual("Cache-Control"u8)        ? "Cache-Control"         :
                  name.SequenceEqual("Content-Range"u8)        ? "Content-Range"         : System.Text.Encoding.ASCII.GetString(name),
            14 => name.SequenceEqual("Accept-Charset"u8)       ? "Accept-Charset"        :
                  name.SequenceEqual("Accept-Ranges"u8)        ? "Accept-Ranges"         :
                  name.SequenceEqual("Content-Length"u8)       ? "Content-Length"        : System.Text.Encoding.ASCII.GetString(name),
            15 => name.SequenceEqual("Accept-Encoding"u8)      ? "Accept-Encoding"       :
                  name.SequenceEqual("Accept-Language"u8)      ? "Accept-Language"       : System.Text.Encoding.ASCII.GetString(name),
            16 => name.SequenceEqual("Content-Encoding"u8)     ? "Content-Encoding"      :
                  name.SequenceEqual("Content-Language"u8)     ? "Content-Language"      :
                  name.SequenceEqual("Content-Location"u8)     ? "Content-Location"      :
                  name.SequenceEqual("WWW-Authenticate"u8)     ? "WWW-Authenticate"      : System.Text.Encoding.ASCII.GetString(name),
            17 => name.SequenceEqual("If-Modified-Since"u8)    ? "If-Modified-Since"     :
                  name.SequenceEqual("Transfer-Encoding"u8)    ? "Transfer-Encoding"     : System.Text.Encoding.ASCII.GetString(name),
            18 => name.SequenceEqual("Proxy-Authenticate"u8)   ? "Proxy-Authenticate"    : System.Text.Encoding.ASCII.GetString(name),
            19 => name.SequenceEqual("If-Unmodified-Since"u8)  ? "If-Unmodified-Since"   :
                  name.SequenceEqual("Proxy-Authorization"u8)  ? "Proxy-Authorization"   : System.Text.Encoding.ASCII.GetString(name),
            25 => name.SequenceEqual("Strict-Transport-Security"u8) ? "Strict-Transport-Security" : System.Text.Encoding.ASCII.GetString(name),
            _  => System.Text.Encoding.ASCII.GetString(name),
        };

    /// <summary>
    /// Returns an interned string for well-known HTTP header values, or allocates
    /// a new string for unknown values. Avoids <see cref="System.Text.Encoding.ASCII"/>
    /// allocations for the most common response header values (Connection tokens,
    /// Transfer-Encoding tokens, Content-Encoding tokens, Cache-Control directives).
    /// </summary>
    /// <remarks>
    /// Uses length as the first discriminator (O(1)) then a byte-sequence comparison —
    /// same technique as <see cref="GetOrCreateHeaderName"/>.
    /// Values are matched case-sensitively; servers should send canonical casing per RFC 9110.
    /// </remarks>
    public static string GetOrCreateHeaderValue(ReadOnlySpan<byte> value)
        => value.Length switch
        {
            1  => value.SequenceEqual("0"u8)          ? "0"          :
                  value.SequenceEqual("1"u8)          ? "1"          : System.Text.Encoding.ASCII.GetString(value),
            2  => value.SequenceEqual("br"u8)         ? "br"         : System.Text.Encoding.ASCII.GetString(value),
            4  => value.SequenceEqual("gzip"u8)       ? "gzip"       :
                  value.SequenceEqual("none"u8)       ? "none"       : System.Text.Encoding.ASCII.GetString(value),
            5  => value.SequenceEqual("close"u8)      ? "close"      :
                  value.SequenceEqual("bytes"u8)      ? "bytes"      : System.Text.Encoding.ASCII.GetString(value),
            6  => value.SequenceEqual("public"u8)     ? "public"     : System.Text.Encoding.ASCII.GetString(value),
            7  => value.SequenceEqual("chunked"u8)    ? "chunked"    :
                  value.SequenceEqual("deflate"u8)    ? "deflate"    :
                  value.SequenceEqual("private"u8)    ? "private"    :
                  value.SequenceEqual("trailer"u8)    ? "trailer"    : System.Text.Encoding.ASCII.GetString(value),
            8  => value.SequenceEqual("compress"u8)   ? "compress"   :
                  value.SequenceEqual("identity"u8)   ? "identity"   :
                  value.SequenceEqual("no-cache"u8)   ? "no-cache"   :
                  value.SequenceEqual("no-store"u8)   ? "no-store"   :
                  value.SequenceEqual("trailers"u8)   ? "trailers"   : System.Text.Encoding.ASCII.GetString(value),
            10 => value.SequenceEqual("keep-alive"u8) ? "keep-alive" : System.Text.Encoding.ASCII.GetString(value),
            _  => System.Text.Encoding.ASCII.GetString(value),
        };

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
