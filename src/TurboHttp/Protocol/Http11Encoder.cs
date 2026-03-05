using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;

namespace TurboHttp.Protocol;

/// <summary>
/// RFC 9112 compliant HTTP/1.1 request encoder with zero-allocation patterns.
/// Writes directly to Span&lt;byte&gt; for maximum efficiency.
/// </summary>
public static class Http11Encoder
{
    // ── Public API ──────────────────────────────────────────────────────────────

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

        // Validate all headers
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

    // ── Request Line ────────────────────────────────────────────────────────────

    private static int WriteRequestLine(HttpRequestMessage request, ref Span<byte> buffer, bool absoluteForm)
    {
        var bytesWritten = 0;

        // Method (GET, POST, etc.)
        bytesWritten += WriteAscii(ref buffer, request.Method.Method);

        // Space
        bytesWritten += WriteBytes(ref buffer, " "u8);

        // Request-target (RFC 9112 Section 3.2)
        var uri = request.RequestUri!;

        // OPTIONS * case (asterisk-form)
        if (request.Method == HttpMethod.Options && uri.PathAndQuery is "*" or "/*")
        {
            bytesWritten += WriteBytes(ref buffer, "*"u8);
        }
        // Absolute-form for proxy requests
        else if (absoluteForm)
        {
            var absoluteUri = uri.GetLeftPart(UriPartial.Query); // Excludes fragment
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

    // ── Host Header ─────────────────────────────────────────────────────────────

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

    // ── Headers ─────────────────────────────────────────────────────────────────

    private static int WriteHeaders(IEnumerable<KeyValuePair<string, IEnumerable<string>>> headers,
        ref Span<byte> buffer,
        bool skipHost)
    {
        var bytesWritten = 0;

        foreach (var header in headers)
        {
            // Skip Host - we handle it separately
            if (skipHost && header.Key.Equals("Host", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // Skip Connection - we handle it separately
            if (header.Key.Equals("Connection", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // Skip connection-specific headers per RFC 9112
            if (IsConnectionSpecificHeader(header.Key))
            {
                continue;
            }

            bytesWritten += WriteHeader(ref buffer, header.Key, header.Value);
        }

        return bytesWritten;
    }

    private static bool IsConnectionSpecificHeader(string headerName)
    {
        // Connection-specific headers that must not be sent per RFC 9112
        return headerName.Equals("TE", StringComparison.OrdinalIgnoreCase) ||
               headerName.Equals("Trailers", StringComparison.OrdinalIgnoreCase) ||
               headerName.Equals("Keep-Alive", StringComparison.OrdinalIgnoreCase) ||
               headerName.Equals("Upgrade", StringComparison.OrdinalIgnoreCase) ||
               headerName.Equals("Proxy-Connection", StringComparison.OrdinalIgnoreCase);
    }

    private static int WriteHeader(ref Span<byte> buffer, string name, IEnumerable<string> values)
    {
        var bytesWritten = 0;

        // Header name
        bytesWritten += WriteAscii(ref buffer, name);
        bytesWritten += WriteBytes(ref buffer, ": "u8);

        // Values joined with comma (RFC 9110 Section 5.3)
        var first = true;
        foreach (var value in values)
        {
            if (!first)
            {
                bytesWritten += WriteBytes(ref buffer, ", "u8);
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
            // RFC 7230 Section 3.3.2: Content-Length MUST NOT be sent when Transfer-Encoding is present
            if (isChunked && header.Key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
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

        // Check if Connection header is already set
        if (headers.Connection.Any(value => value.Equals("close", StringComparison.OrdinalIgnoreCase)))
        {
            bytesWritten += WriteBytes(ref buffer, "Connection: close\r\n"u8);
            return bytesWritten;
        }

        // Other connection values - write them with keep-alive
        bytesWritten += WriteBytes(ref buffer, "Connection: "u8);

        var first = true;
        foreach (var value in headers.Connection)
        {
            if (!first)
            {
                bytesWritten += WriteBytes(ref buffer, ", "u8);
            }

            bytesWritten += WriteAscii(ref buffer, value);
            first = false;
        }

        if (!first)
        {
            bytesWritten += WriteBytes(ref buffer, ", "u8);
        }

        bytesWritten += WriteBytes(ref buffer, "keep-alive\r\n"u8);

        return bytesWritten;
    }

    // ── Body ────────────────────────────────────────────────────────────────────

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

    // ── Low-Level Write Utilities ───────────────────────────────────────────────

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

        // Max int32 is 10 digits
        Span<byte> temp = stackalloc byte[10];
        var pos = temp.Length;

        do
        {
            temp[--pos] = (byte)('0' + value % 10);
            value /= 10;
        } while (value > 0);

        var length = temp.Length - pos;
        temp[pos..].CopyTo(buffer);
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

        // Max int32 is 8 hex digits
        Span<byte> temp = stackalloc byte[8];
        var pos = temp.Length;

        if (value == 0)
        {
            buffer[0] = (byte)'0';
            buffer = buffer[1..];
            return 1;
        }

        while (value > 0)
        {
            var digit = value % 16;
            temp[--pos] = (byte)(digit < 10 ? '0' + digit : 'a' + (digit - 10));
            value /= 16;
        }

        var length = temp.Length - pos;
        temp[pos..].CopyTo(buffer);
        buffer = buffer[length..];

        return length;
    }

    // ── Validation ──────────────────────────────────────────────────────────────

    private static void ValidateMethod(string method)
    {
        if (method.Any(char.IsLower))
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
        // RFC 7233 §2.1: bytes-range-spec = first-byte-pos "-" [last-byte-pos]
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