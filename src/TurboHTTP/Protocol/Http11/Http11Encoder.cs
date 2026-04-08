using System.Net.Http.Headers;
using System.Text;
using TurboHTTP.Protocol.Semantics;

namespace TurboHTTP.Protocol.Http11;

/// <summary>
/// RFC 9112 compliant HTTP/1.1 request encoder with zero-allocation patterns.
/// Writes directly to Span&lt;byte&gt; for maximum efficiency.
/// </summary>
public static class Http11Encoder
{
    /// <summary>
    /// Encodes an HTTP/1.1 request directly into a span.
    /// Zero-allocation - writes directly to the provided buffer.
    /// </summary>
    /// <param name="request">The HTTP request to encode</param>
    /// <param name="buffer">Target buffer (advanced as data is written)</param>
    /// <param name="absoluteForm">If true, use absolute-form URI for proxy requests</param>
    /// <returns>Total bytes written</returns>
    public static int Encode(HttpRequestMessage request, ref Span<byte> buffer, bool absoluteForm = false)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.RequestUri);

        // Validate method before encoding
        ValidateMethod(request.Method.Method);

        // Validate all headers (injection prevention + RFC compliance)
        ValidateHeaders(request.Headers);
        if (request.Content != null)
        {
            ValidateHeaders(request.Content.Headers);
        }

        var bytesWritten = 0;

        // 1. Request-Line (RFC 9112 Section 3)
        bytesWritten += WriteRequestLine(request, ref buffer, absoluteForm);

        // 2. Host header (RFC 9112 Section 5.4 - MUST be present and first)
        bytesWritten += WriteHostHeader(request.RequestUri, ref buffer);

        // Check if chunked encoding is requested
        var isChunked = request.Headers.TransferEncodingChunked == true;

        // 3. Accept-Encoding (RFC 9110 §8.4: advertise supported decodings unless already set)
        bytesWritten += WriteAcceptEncodingIfNeeded(request.Headers, ref buffer);

        // 4. Request headers (excluding Host which we already wrote)
        bytesWritten += WriteHeaders(request.Headers, ref buffer, skipHost: true);

        // 5. Content headers (if body present)
        if (request.Content != null)
        {
            // Ensure Content-Length is set for content with known length
            // This is required for HTTP/1.1 requests with bodies (unless chunked)
            if (!isChunked && request.Content.Headers.ContentLength == null)
            {
                using var stream = request.Content.ReadAsStream();
                if (stream.CanSeek)
                {
                    request.Content.Headers.ContentLength = stream.Length;
                }
            }

            bytesWritten += WriteContentHeaders(request.Content.Headers, ref buffer, isChunked);
        }

        // 6. Connection header (if not already set, default to keep-alive)
        bytesWritten += WriteConnectionHeaderIfNeeded(request.Headers, ref buffer);

        // 7. Header/body separator
        bytesWritten += WriteCrlf(ref buffer);

        // 8. Body (if present)
        if (request.Content != null)
        {
            if (isChunked)
            {
                bytesWritten += WriteChunkedBody(request.Content, ref buffer);
            }
            else
            {
                bytesWritten += WriteBody(request.Content, ref buffer);
            }
        }

        return bytesWritten;
    }

    private static int WriteRequestLine(HttpRequestMessage request, ref Span<byte> buffer, bool absoluteForm)
    {
        var bytesWritten = 0;

        // Method (GET, POST, etc.)
        bytesWritten += WriteAscii(ref buffer, request.Method.Method);

        // Space
        bytesWritten += WriteBytes(ref buffer, WellKnownHeaders.Space);

        // Request-target (RFC 9112 Section 3.2)
        var uri = request.RequestUri!;

        // CONNECT uses authority-form (RFC 9110 §9.3.6: host:port, always include port)
        if (request.Method == HttpMethod.Connect)
        {
            var authority = UriSanitizer.FormatAuthorityWithPort(uri);
            bytesWritten += WriteAscii(ref buffer, authority);
        }
        // OPTIONS * case (asterisk-form)
        else if (request.Method == HttpMethod.Options && uri.PathAndQuery is "*" or "/*")
        {
            bytesWritten += WriteBytes(ref buffer, "*"u8);
        }
        // Absolute-form for proxy requests (RFC 9110 §4.2.4: strip userinfo)
        else if (absoluteForm)
        {
            var absoluteUri = UriSanitizer.FormatAbsoluteWithoutUserInfo(uri);
            bytesWritten += WriteAscii(ref buffer, absoluteUri);
        }
        // Origin-form (normal case) - path and query without fragment
        else
        {
            var pathAndQuery = uri.PathAndQuery;
            if (string.IsNullOrEmpty(pathAndQuery) || pathAndQuery == "/")
            {
                pathAndQuery = "/";
            }

            bytesWritten += WriteAscii(ref buffer, pathAndQuery);
        }

        // HTTP/1.1 and CRLF
        bytesWritten += WriteBytes(ref buffer, " HTTP/1.1\r\n"u8);

        return bytesWritten;
    }

    private static int WriteHostHeader(Uri uri, ref Span<byte> buffer)
    {
        var bytesWritten = 0;

        bytesWritten += WriteBytes(ref buffer, "Host: "u8);

        // uri.Host already includes brackets for IPv6 addresses
        bytesWritten += WriteAscii(ref buffer, uri.Host);

        // Include port if non-default
        if (!uri.IsDefaultPort)
        {
            bytesWritten += WriteBytes(ref buffer, ":"u8);
            bytesWritten += WriteInt(ref buffer, uri.Port);
        }

        bytesWritten += WriteCrlf(ref buffer);

        return bytesWritten;
    }

    private static int WriteHeaders(IEnumerable<KeyValuePair<string, IEnumerable<string>>> headers,
        ref Span<byte> buffer,
        bool skipHost)
    {
        var bytesWritten = 0;

        foreach (var header in headers)
        {
            // Skip Host - we handle it separately
            if (skipHost && header.Key.Equals(WellKnownHeaders.Names.Host, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // Skip Connection - we handle it separately
            if (header.Key.Equals(WellKnownHeaders.Names.Connection, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // Skip connection-specific headers per RFC 9112
            if (IsConnectionSpecificHeader(header.Key))
            {
                continue;
            }

            // RFC 9112 §7.4: TE MUST NOT include "chunked" — filter it out
            if (header.Key.Equals("TE", StringComparison.OrdinalIgnoreCase))
            {
                bytesWritten += WriteTeHeader(ref buffer, header.Value);
                continue;
            }

            bytesWritten += WriteHeader(ref buffer, header.Key, header.Value);
        }

        return bytesWritten;
    }

    /// <summary>
    /// Writes the TE header with "chunked" excluded per RFC 9112 §7.4.
    /// Returns 0 if no valid TE values remain after filtering.
    /// Zero-allocation: iterates values as char spans, writes tokens directly
    /// to the buffer using a speculative write that is discarded if empty.
    /// </summary>
    private static int WriteTeHeader(ref Span<byte> buffer, IEnumerable<string> values)
    {
        // Speculative write: save the buffer slice before writing the header name.
        // If no valid tokens are found we restore it — cheap struct copy on the stack.
        var savedBuffer = buffer;

        var bytesWritten = 0;
        bytesWritten += WriteAscii(ref buffer, "TE");
        bytesWritten += WriteBytes(ref buffer, WellKnownHeaders.ColonSpace);

        var first = true;
        foreach (var value in values)
        {
            var span = value.AsSpan();
            var start = 0;
            while (true)
            {
                var comma = span[start..].IndexOf(',');
                var end = comma >= 0 ? start + comma : span.Length;
                var token = span[start..end].Trim();

                if (token.Length > 0 &&
                    !token.Equals("chunked", StringComparison.OrdinalIgnoreCase))
                {
                    if (!first)
                    {
                        bytesWritten += WriteBytes(ref buffer, WellKnownHeaders.CommaSpace);
                    }

                    bytesWritten += WriteAscii(ref buffer, token);
                    first = false;
                }

                if (comma < 0)
                {
                    break;
                }

                start = end + 1;
            }
        }

        if (first)
        {
            // No valid tokens — discard the speculative "TE: " write
            buffer = savedBuffer;
            return 0;
        }

        bytesWritten += WriteCrlf(ref buffer);
        return bytesWritten;
    }

    private static bool IsConnectionSpecificHeader(string headerName)
    {
        // Connection-specific headers that must not be sent per RFC 9112
        // Note: TE is handled separately — it IS a hop-by-hop header but is valid to send
        // per RFC 9112 §7.4 (with "chunked" excluded and "TE" listed in Connection).
        return headerName.Equals("Trailers", StringComparison.OrdinalIgnoreCase) ||
               headerName.Equals("Keep-Alive", StringComparison.OrdinalIgnoreCase) ||
               headerName.Equals("Upgrade", StringComparison.OrdinalIgnoreCase) ||
               headerName.Equals("Proxy-Connection", StringComparison.OrdinalIgnoreCase);
    }

    private static int WriteHeader(ref Span<byte> buffer, string name, IEnumerable<string> values)
    {
        var bytesWritten = 0;

        // Header name
        bytesWritten += WriteAscii(ref buffer, name);
        bytesWritten += WriteBytes(ref buffer, WellKnownHeaders.ColonSpace);

        // Values joined with comma (RFC 9110 Section 5.3)
        var first = true;
        foreach (var value in values)
        {
            if (!first)
            {
                bytesWritten += WriteBytes(ref buffer, WellKnownHeaders.CommaSpace);
            }

            bytesWritten += WriteAscii(ref buffer, value);
            first = false;
        }

        bytesWritten += WriteCrlf(ref buffer);

        return bytesWritten;
    }

    private static int WriteContentHeaders(HttpContentHeaders headers, ref Span<byte> buffer, bool isChunked)
    {
        var bytesWritten = 0;

        foreach (var header in headers)
        {
            // RFC 9112 §6.1: Content-Length MUST NOT be sent when Transfer-Encoding is present
            if (isChunked && header.Key.Equals(WellKnownHeaders.Names.ContentLength, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            bytesWritten += WriteHeader(ref buffer, header.Key, header.Value);
        }

        return bytesWritten;
    }

    private static int WriteAcceptEncodingIfNeeded(HttpRequestHeaders headers, ref Span<byte> buffer)
    {
        // RFC 9110 §8.4: Advertise supported content-encodings unless caller already set the header.
        if (headers.AcceptEncoding.Count > 0)
        {
            return 0;
        }

        return WriteBytes(ref buffer, "Accept-Encoding: gzip, deflate, br\r\n"u8);
    }

    private static int WriteConnectionHeaderIfNeeded(HttpRequestHeaders headers, ref Span<byte> buffer)
    {
        var bytesWritten = 0;

        // RFC 9112 §7.4: Detect if TE header has non-chunked values that need listing
        var hasTeValues = HasNonChunkedTeValues(headers);

        // Check if Connection header is already set
        if (headers.Connection.Any(value => value.Equals("close", StringComparison.OrdinalIgnoreCase)))
        {
            // Even with "close", we must list TE if present (RFC 9112 §7.4)
            if (hasTeValues && !headers.Connection.Any(v => v.Equals("TE", StringComparison.OrdinalIgnoreCase)))
            {
                bytesWritten += WriteBytes(ref buffer, "Connection: close, TE\r\n"u8);
            }
            else
            {
                bytesWritten += WriteBytes(ref buffer, "Connection: close\r\n"u8);
            }

            return bytesWritten;
        }

        // Other connection values - write them with keep-alive
        bytesWritten += WriteBytes(ref buffer, "Connection: "u8);

        var first = true;
        var alreadyHasTe = false;

        foreach (var value in headers.Connection)
        {
            if (!first)
            {
                bytesWritten += WriteBytes(ref buffer, WellKnownHeaders.CommaSpace);
            }

            bytesWritten += WriteAscii(ref buffer, value);
            first = false;

            if (value.Equals("TE", StringComparison.OrdinalIgnoreCase))
            {
                alreadyHasTe = true;
            }
        }

        // RFC 9112 §7.4: auto-add "TE" to Connection if TE header is present and not already listed
        if (hasTeValues && !alreadyHasTe)
        {
            if (!first)
            {
                bytesWritten += WriteBytes(ref buffer, WellKnownHeaders.CommaSpace);
            }

            bytesWritten += WriteBytes(ref buffer, "TE"u8);
            first = false;
        }

        if (!first)
        {
            bytesWritten += WriteBytes(ref buffer, WellKnownHeaders.CommaSpace);
        }

        bytesWritten += WriteBytes(ref buffer, WellKnownHeaders.KeepAlive);
        bytesWritten += WriteCrlf(ref buffer);

        return bytesWritten;
    }

    /// <summary>
    /// Returns true if the request has a TE header with at least one non-chunked value.
    /// </summary>
    private static bool HasNonChunkedTeValues(HttpRequestHeaders headers)
    {
        if (!headers.TryGetValues("TE", out var teValues))
        {
            return false;
        }

        foreach (var value in teValues)
        {
            var span = value.AsSpan();
            var start = 0;
            while (true)
            {
                var comma = span[start..].IndexOf(',');
                var end = comma >= 0 ? start + comma : span.Length;
                var token = span[start..end].Trim();
                if (token.Length > 0 &&
                    !token.Equals("chunked", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
                if (comma < 0) { break; }
                start = end + 1;
            }
        }

        return false;
    }

    private static int WriteBody(HttpContent content, ref Span<byte> buffer)
    {
        using var stream = content.ReadAsStream();

        // If Content-Length is known, validate we have enough buffer space
        if (content.Headers.ContentLength.HasValue)
        {
            var contentLength = content.Headers.ContentLength.Value;
            if (buffer.Length < contentLength)
            {
                throw new ArgumentException(
                    $"Buffer too small for body: need {contentLength} bytes, have {buffer.Length} bytes available",
                    nameof(buffer));
            }
        }

        var total = 0;

        while (buffer.Length > 0)
        {
            var read = stream.Read(buffer);
            if (read == 0)
            {
                break;
            }

            buffer = buffer[read..];
            total += read;
        }

        return total;
    }

    private static int WriteChunkedBody(HttpContent content, ref Span<byte> buffer)
    {
        using var stream = content.ReadAsStream();
        var total = 0;
        const int chunkSize = 8192; // 8KB chunks

        var chunkBuffer = new byte[chunkSize];

        while (true)
        {
            var read = stream.Read(chunkBuffer, 0, chunkSize);
            if (read == 0)
            {
                break;
            }

            // Write chunk size in hex
            total += WriteHex(ref buffer, read);
            total += WriteCrlf(ref buffer);

            // Write chunk data
            total += WriteBytes(ref buffer, chunkBuffer.AsSpan(0, read));
            total += WriteCrlf(ref buffer);
        }

        // Write final chunk: 0\r\n\r\n
        total += WriteBytes(ref buffer, "0\r\n\r\n"u8);

        return total;
    }

    /// <summary>
    /// Writes bytes directly to span and advances it.
    /// </summary>
    private static int WriteBytes(ref Span<byte> buffer, ReadOnlySpan<byte> data)
    {
        data.CopyTo(buffer);
        buffer = buffer[data.Length..];
        return data.Length;
    }

    /// <summary>
    /// Writes ASCII string directly to span and advances it.
    /// </summary>
    private static int WriteAscii(ref Span<byte> buffer, string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return 0;
        }

        var written = Encoding.ASCII.GetBytes(value.AsSpan(), buffer);
        buffer = buffer[written..];
        return written;
    }

    /// <summary>
    /// Writes an ASCII char-span directly to the byte buffer without allocating a string.
    /// </summary>
    private static int WriteAscii(ref Span<byte> buffer, ReadOnlySpan<char> value)
    {
        if (value.IsEmpty)
        {
            return 0;
        }

        var written = Encoding.ASCII.GetBytes(value, buffer);
        buffer = buffer[written..];
        return written;
    }

    /// <summary>
    /// Writes CRLF and advances span.
    /// </summary>
    private static int WriteCrlf(ref Span<byte> buffer)
    {
        buffer[0] = (byte)'\r';
        buffer[1] = (byte)'\n';
        buffer = buffer[2..];
        return 2;
    }

    /// <summary>
    /// Writes an integer as ASCII digits without heap allocation.
    /// </summary>
    private static int WriteInt(ref Span<byte> buffer, int value)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "Value must be non-negative");
        }

        // Write digits in reverse order directly into buffer, then reverse in-place
        var length = 0;

        do
        {
            buffer[length++] = (byte)('0' + value % 10);
            value /= 10;
        } while (value > 0);

        buffer[..length].Reverse();
        buffer = buffer[length..];

        return length;
    }

    /// <summary>
    /// Writes an integer as hexadecimal ASCII without heap allocation.
    /// </summary>
    private static int WriteHex(ref Span<byte> buffer, int value)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "Value must be non-negative");
        }

        if (value == 0)
        {
            buffer[0] = (byte)'0';
            buffer = buffer[1..];
            return 1;
        }

        // Write hex digits in reverse order directly into buffer, then reverse in-place
        var length = 0;

        while (value > 0)
        {
            var digit = value % 16;
            buffer[length++] = (byte)(digit < 10 ? '0' + digit : 'a' + (digit - 10));
            value /= 16;
        }

        buffer[..length].Reverse();
        buffer = buffer[length..];

        return length;
    }

    private static void ValidateMethod(string method)
    {
        if (method.AsSpan().IndexOfAnyInRange('a', 'z') >= 0)
        {
            throw new ArgumentException($"HTTP/1.1 method must be uppercase: {method}", nameof(method));
        }
    }

    private static void ValidateHeaders(IEnumerable<KeyValuePair<string, IEnumerable<string>>> headers)
    {
        foreach (var header in headers)
        {
            foreach (var value in header.Value)
            {
                ValidateHeaderValue(header.Key, value);
            }
        }
    }

    private static void ValidateHeaders(HttpContentHeaders headers)
    {
        foreach (var header in headers)
        {
            foreach (var value in header.Value)
            {
                ValidateHeaderValue(header.Key, value);
            }
        }
    }

    private static void ValidateHeaderValue(string name, string value)
    {
        if (value.AsSpan().ContainsAny('\r', '\n', '\0'))
        {
            throw new ArgumentException($"Header '{name}' contains invalid characters (CR/LF/NUL)", name);
        }

        if (name.Equals("Range", StringComparison.OrdinalIgnoreCase))
        {
            ValidateRangeValue(value);
        }
    }

    private static void ValidateRangeValue(string value)
    {
        // RFC 9110 §14.1.1: bytes-range-spec = first-byte-pos "-" [last-byte-pos]
        // suffix-byte-range-spec = "-" suffix-length
        // All positions must consist only of DIGIT characters.
        if (!value.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"Invalid Range header value: '{value}' (must start with 'bytes=')", "Range");
        }

        var rangeSpec = value["bytes=".Length..];
        var ranges = rangeSpec.Split(',');

        foreach (var range in ranges)
        {
            var trimmed = range.AsSpan().Trim();
            if (trimmed.IsEmpty)
            {
                continue;
            }

            var dashIdx = trimmed.IndexOf('-');
            if (dashIdx < 0)
            {
                throw new ArgumentException($"Invalid Range header value: '{value}' (missing '-' in range spec)", "Range");
            }

            var first = trimmed[..dashIdx];
            var last = trimmed[(dashIdx + 1)..];

            if (first.IsEmpty && last.IsEmpty)
            {
                throw new ArgumentException($"Invalid Range header value: '{value}' (empty range spec)", "Range");
            }

            foreach (var ch in first)
            {
                if (!char.IsAsciiDigit(ch))
                {
                    throw new ArgumentException($"Invalid Range header value: '{value}' (non-digit in byte position)", "Range");
                }
            }

            foreach (var ch in last)
            {
                if (!char.IsAsciiDigit(ch))
                {
                    throw new ArgumentException($"Invalid Range header value: '{value}' (non-digit in byte position)", "Range");
                }
            }
        }
    }
}