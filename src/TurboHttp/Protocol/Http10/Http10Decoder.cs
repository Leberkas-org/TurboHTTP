using System.Net;
using System.Text;

namespace TurboHttp.Protocol.Http10;

public sealed class Http10Decoder
{
    private const int DefaultMaxHeaderSize = 16 * 1024;       // 16 KB
    private const int DefaultMaxTotalHeaderSize = 64 * 1024;  // 64 KB

    private static readonly byte[] HttpSlashPrefix = "HTTP/"u8.ToArray();
    private static readonly Encoding Iso88591 = Encoding.GetEncoding("iso-8859-1");

    private readonly int _maxHeaderSize;
    private readonly int _maxTotalHeaderSize;

    private ReadOnlyMemory<byte> _remainder = ReadOnlyMemory<byte>.Empty;
    private bool _isHttp09;
    private int? _pendingContentLength;  // Non-null if waiting for body data due to Content-Length

    /// <summary>
    /// Returns true if the decoder is waiting for body data due to a Content-Length header.
    /// This is used to detect Content-Length mismatches when the connection abruptly closes.
    /// </summary>
    public bool IsWaitingForContentLength => _pendingContentLength.HasValue;

    public Http10Decoder(int maxHeaderSize = DefaultMaxHeaderSize, int maxTotalHeaderSize = DefaultMaxTotalHeaderSize)
    {
        _maxHeaderSize = maxHeaderSize;
        _maxTotalHeaderSize = maxTotalHeaderSize;
    }

    public bool TryDecode(ReadOnlyMemory<byte> incomingData, out HttpResponseMessage? response)
    {
        response = null;
        var working = Combine(_remainder, incomingData);
        _remainder = ReadOnlyMemory<byte>.Empty;

        // HTTP/0.9 continuation: accumulate body until EOF
        if (_isHttp09)
        {
            _remainder = working;
            return false;
        }

        // HTTP/0.9 detection (RFC 1945 §3.1): if first bytes do not start with "HTTP/"
        if (!working.IsEmpty)
        {
            var span = working.Span;
            if (span.Length >= HttpSlashPrefix.Length)
            {
                // Enough bytes to decide: not HTTP/ means HTTP/0.9
                if (!span.StartsWith(HttpSlashPrefix))
                {
                    _isHttp09 = true;
                    _remainder = working;
                    return false;
                }
            }
            else
            {
                // Not enough bytes yet — check if current bytes are a valid prefix of "HTTP/"
                if (!HttpSlashPrefix.AsSpan(0, span.Length).SequenceEqual(span))
                {
                    _isHttp09 = true;
                    _remainder = working;
                    return false;
                }
            }
        }

        var headerEnd = FindHeaderEnd(working.Span);
        if (headerEnd < 0)
        {
            _remainder = working;
            return false;
        }

        var headerBytes = working[..headerEnd].ToArray();
        var lines = SplitHeaderLines(headerBytes);
        if (lines.Length == 0)
        {
            return false;
        }

        ValidateStatusLine(lines[0]);
        var headers = ParseHeaders(lines[1..], _maxHeaderSize, _maxTotalHeaderSize);
        var bodyStart = headerEnd + GetHeaderDelimiterLength(working.Span, headerEnd);
        var bodyData = working[bodyStart..];

        var statusCode = ParseStatusCode(lines[0]);

        // No-body responses: 204 and 304 always have empty body (RFC 1945 §7)
        if (statusCode is 204 or 304)
        {
            response = BuildResponse(lines[0], headers, []);
            _pendingContentLength = null;
            return true;
        }

        var contentLength = GetContentLength(headers);
        if (contentLength.HasValue)
        {
            if (bodyData.Length < contentLength.Value)
            {
                _remainder = working;
                _pendingContentLength = contentLength.Value;
                return false;
            }

            response = BuildResponse(lines[0], headers, bodyData.Span[..contentLength.Value].ToArray());
            _pendingContentLength = null;
            return true;
        }

        response = BuildResponse(lines[0], headers, bodyData.ToArray());
        _pendingContentLength = null;
        return true;
    }

    public bool TryDecodeEof(out HttpResponseMessage? response)
    {
        response = null;
        if (_remainder.IsEmpty)
        {
            // HTTP/0.9 with zero bytes before EOF: empty body
            if (_isHttp09)
            {
                response = BuildHttp09Response([]);
                _isHttp09 = false;
                _pendingContentLength = null;
                return true;
            }

            return false;
        }

        // HTTP/0.9: entire remainder is body
        if (_isHttp09)
        {
            response = BuildHttp09Response(_remainder.ToArray());
            _remainder = ReadOnlyMemory<byte>.Empty;
            _isHttp09 = false;
            _pendingContentLength = null;
            return true;
        }

        var span = _remainder.Span;
        var headerEnd = FindHeaderEnd(span);
        if (headerEnd < 0)
        {
            return false;
        }

        var headerBytes = _remainder[..headerEnd].ToArray();
        var lines = SplitHeaderLines(headerBytes);
        if (lines.Length == 0)
        {
            return false;
        }

        ValidateStatusLine(lines[0]);
        var headers = ParseHeaders(lines[1..], _maxHeaderSize, _maxTotalHeaderSize);
        var index = headerEnd + GetHeaderDelimiterLength(span, headerEnd);
        var body = _remainder[index..].ToArray();

        // RFC 1945: If a Content-Length was declared but EOF arrived after receiving partial
        // body data, it's a truncation error. When body is empty, allow it — connection
        // may have been closed cleanly after headers (e.g. HEAD response, 204, or abrupt
        // close already detected via CloseSignalItem.AbruptClose before reaching here).
        var contentLength = GetContentLength(headers);
        if (contentLength.HasValue && body.Length > 0 && body.Length < contentLength.Value)
        {
            throw new HttpDecoderException(HttpDecoderError.InvalidContentLength,
                $"Content-Length mismatch: expected {contentLength.Value} bytes but received {body.Length} bytes before EOF.");
        }

        response = BuildResponse(lines[0], headers, body);
        _remainder = ReadOnlyMemory<byte>.Empty;
        _pendingContentLength = null;
        return true;
    }

    /// <summary>
    /// Attempts to decode an HTTP/1.0 response to a CONNECT request.
    /// A successful (2xx) CONNECT response has no body (tunnel begins),
    /// regardless of Content-Length. Non-2xx responses are decoded normally.
    /// </summary>
    /// <remarks>
    /// RFC 9110 §9.3.6: A server MUST NOT send Content-Length or Transfer-Encoding
    /// in a 2xx (Successful) response to CONNECT. A client MUST ignore any such
    /// header fields received in a successful CONNECT response.
    /// </remarks>
    public bool TryDecodeConnect(ReadOnlyMemory<byte> incomingData, out HttpResponseMessage? response)
    {
        response = null;
        var working = Combine(_remainder, incomingData);
        _remainder = ReadOnlyMemory<byte>.Empty;

        var headerEnd = FindHeaderEnd(working.Span);
        if (headerEnd < 0)
        {
            _remainder = working;
            return false;
        }

        var headerBytes = working[..headerEnd].ToArray();
        var lines = SplitHeaderLines(headerBytes);
        if (lines.Length == 0)
        {
            return false;
        }

        ValidateStatusLine(lines[0]);
        var headers = ParseHeaders(lines[1..], _maxHeaderSize, _maxTotalHeaderSize);
        var bodyStart = headerEnd + GetHeaderDelimiterLength(working.Span, headerEnd);
        var bodyData = working[bodyStart..];

        var statusCode = ParseStatusCode(lines[0]);

        // CONNECT 2xx: body length = 0 (tunnel begins)
        if (statusCode is >= 200 and < 300)
        {
            response = BuildResponse(lines[0], headers, []);
            _pendingContentLength = null;
            return true;
        }

        // Non-2xx: normal body handling (same as TryDecode)
        if (statusCode is 204 or 304)
        {
            response = BuildResponse(lines[0], headers, []);
            _pendingContentLength = null;
            return true;
        }

        var contentLength = GetContentLength(headers);
        if (contentLength.HasValue)
        {
            if (bodyData.Length < contentLength.Value)
            {
                _remainder = working;
                _pendingContentLength = contentLength.Value;
                return false;
            }

            response = BuildResponse(lines[0], headers, bodyData.Span[..contentLength.Value].ToArray());
            _pendingContentLength = null;
            return true;
        }

        response = BuildResponse(lines[0], headers, bodyData.ToArray());
        _pendingContentLength = null;
        return true;
    }

    public void Reset()
    {
        _remainder = ReadOnlyMemory<byte>.Empty;
        _isHttp09 = false;
        _pendingContentLength = null;
    }

    private static void ValidateStatusLine(string statusLine)
    {
        var parts = statusLine.Split(' ', 3);
        if (parts.Length < 2 || !int.TryParse(parts[1], out var code))
        {
            throw new HttpDecoderException(HttpDecoderError.InvalidStatusLine, $"Line: '{statusLine}'.");
        }

        if (code is < 100 or > 999)
        {
            throw new HttpDecoderException(HttpDecoderError.InvalidStatusLine,
                $"Status code {code} is out of the valid range 100–999.");
        }
    }

    private static int ParseStatusCode(string statusLine)
    {
        var parts = statusLine.Split(' ', 3);
        return parts.Length >= 2 && int.TryParse(parts[1], out var code) ? code : 500;
    }

    /// <summary>
    /// Validates and returns Content-Length from headers.
    /// Throws on negative values or conflicting multiple values.
    /// </summary>
    private static int? GetContentLength(Dictionary<string, List<string>> headers)
    {
        if (!headers.TryGetValue(WellKnownHeaders.Names.ContentLength, out var clValues) ||
            clValues.Count == 0)
        {
            return null;
        }

        // RFC 1945: Multiple Content-Length with different values is an error
        if (clValues.Count > 1)
        {
            var first = clValues[0];
            for (var i = 1; i < clValues.Count; i++)
            {
                if (!clValues[i].Equals(first, StringComparison.Ordinal))
                {
                    throw new HttpDecoderException(HttpDecoderError.MultipleContentLengthValues,
                        $"Values '{first}' and '{clValues[i]}' conflict.");
                }
            }
        }

        if (!int.TryParse(clValues[0], out var len))
        {
            throw new HttpDecoderException(HttpDecoderError.InvalidContentLength,
                $"Value: '{clValues[0]}'.");
        }

        if (len < 0)
        {
            throw new HttpDecoderException(HttpDecoderError.InvalidContentLength,
                $"Value {len} is negative.");
        }

        return len;
    }

    private static Dictionary<string, List<string>> ParseHeaders(string[] lines, int maxHeaderSize, int maxTotalHeaderSize)
    {
        var headers = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        string? lastHeader = null;
        var totalSize = 0;

        foreach (var rawLine in lines)
        {
            if (string.IsNullOrWhiteSpace(rawLine))
            {
                continue;
            }

            // Obs-fold continuation (RFC 1945 §4.2): line starting with SP or HT
            if ((rawLine[0] == ' ' || rawLine[0] == '\t') && lastHeader != null)
            {
                var lastValues = headers[lastHeader];
                var lastValue = lastValues[^1];
                var foldedValue = lastValue + " " + rawLine.Trim();
                lastValues[^1] = foldedValue;

                // Re-check single header size after fold: name + ": " + updated value
                var foldedHeaderSize = Iso88591.GetByteCount(lastHeader)
                    + 2 // ": "
                    + Iso88591.GetByteCount(foldedValue);
                if (foldedHeaderSize > maxHeaderSize)
                {
                    throw new HttpDecoderException(HttpDecoderError.HeaderTooLarge,
                        $"Header '{lastHeader}' is {foldedHeaderSize} bytes; limit is {maxHeaderSize}.");
                }

                // Add fold contribution to total
                var foldContribution = Iso88591.GetByteCount(rawLine.Trim()) + 1; // " " + trimmed
                totalSize += foldContribution;
                if (totalSize > maxTotalHeaderSize)
                {
                    throw new HttpDecoderException(HttpDecoderError.TotalHeadersTooLarge,
                        $"Total header size is {totalSize} bytes; limit is {maxTotalHeaderSize}.");
                }

                continue;
            }

            var colon = rawLine.IndexOf(':');
            if (colon <= 0)
            {
                throw new HttpDecoderException(HttpDecoderError.InvalidHeader);
            }

            var name = rawLine[..colon];

            // Validate header name: no spaces allowed
            if (name.Contains(' '))
            {
                throw new HttpDecoderException(HttpDecoderError.InvalidFieldName);
            }

            name = name.Trim();
            var value = rawLine[(colon + 1)..].Trim();

            // Check single header size: name + ": " + value
            var headerSize = Iso88591.GetByteCount(name)
                + 2 // ": "
                + Iso88591.GetByteCount(value);
            if (headerSize > maxHeaderSize)
            {
                throw new HttpDecoderException(HttpDecoderError.HeaderTooLarge,
                    $"Header '{name}' is {headerSize} bytes; limit is {maxHeaderSize}.");
            }

            totalSize += headerSize;
            if (totalSize > maxTotalHeaderSize)
            {
                throw new HttpDecoderException(HttpDecoderError.TotalHeadersTooLarge,
                    $"Total header size is {totalSize} bytes; limit is {maxTotalHeaderSize}.");
            }

            if (!headers.TryGetValue(name, out var value1))
            {
                value1 = [];
                headers[name] = value1;
            }

            value1.Add(value);
            lastHeader = name;
        }

        return headers;
    }

    private static readonly HashSet<string> ContentHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Content-Type", WellKnownHeaders.Names.ContentLength,
        WellKnownHeaders.Names.ContentEncoding, "Content-Language", "Content-Location", "Content-MD5",
        "Content-Range", "Content-Disposition", "Expires", "Last-Modified"
    };

    /// <summary>
    /// Builds an HTTP/0.9 Simple-Response: status 200, no headers, body until EOF.
    /// </summary>
    private static HttpResponseMessage BuildHttp09Response(byte[] body)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Version = new Version(0, 9),
            Content = new ByteArrayContent(body)
        };
    }

    private static HttpResponseMessage BuildResponse(string statusLine, Dictionary<string, List<string>> headers,
        byte[] body)
    {
        var parts = statusLine.Split(' ', 3);
        var statusCode = 500;
        if (parts.Length >= 2 && int.TryParse(parts[1], out var code))
        {
            statusCode = code;
        }

        var reasonPhrase = parts.Length > 2 ? parts[2] : string.Empty;
        var response = new HttpResponseMessage((HttpStatusCode)statusCode)
        {
            ReasonPhrase = reasonPhrase,
            Version = HttpVersion.Version10
        };

        var content = new ByteArrayContent(body);
        response.Content = content;

        foreach (var (name, values) in headers)
        {
            foreach (var value in values)
            {
                if (ContentHeaders.Contains(name))
                {
                    content.Headers.TryAddWithoutValidation(name, value);
                }
                else
                {
                    response.Headers.TryAddWithoutValidation(name, value);
                }
            }
        }

        return response;
    }

    private static ReadOnlyMemory<byte> Combine(ReadOnlyMemory<byte> a, ReadOnlyMemory<byte> b)
    {
        if (a.IsEmpty)
        {
            return b;
        }

        if (b.IsEmpty)
        {
            return a;
        }

        var merged = new byte[a.Length + b.Length];
        a.Span.CopyTo(merged.AsSpan());
        b.Span.CopyTo(merged.AsSpan(a.Length));
        return merged;
    }

    private static int FindHeaderEnd(ReadOnlySpan<byte> span)
    {
        for (var i = 0; i < span.Length - 1; i++)
        {
            if ((span[i] == '\r' && span[i + 1] == '\n' && i + 3 < span.Length && span[i + 2] == '\r' &&
                 span[i + 3] == '\n') ||
                (span[i] == '\n' && span[i + 1] == '\n'))
            {
                return i;
            }
        }

        return -1;
    }

    private static int GetHeaderDelimiterLength(ReadOnlySpan<byte> span, int headerEnd)
    {
        if (headerEnd + 3 < span.Length && span[headerEnd] == '\r' && span[headerEnd + 1] == '\n' &&
            span[headerEnd + 2] == '\r' && span[headerEnd + 3] == '\n')
        {
            return 4;
        }

        return 2;
    }

    private static string[] SplitHeaderLines(byte[] headerBytes)
    {
        var headerText = Iso88591.GetString(headerBytes);
        return headerText.Split(["\r\n", "\n"], StringSplitOptions.None);
    }
}