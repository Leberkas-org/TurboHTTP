using System.Buffers;
using System.Text;

namespace TurboHTTP.Protocol.Http10;

internal sealed class Decoder
{
    private const int DefaultMaxHeaderSize = 16 * 1024;       // 16 KB
    private const int DefaultMaxTotalHeaderSize = 64 * 1024;  // 64 KB

    private static ReadOnlySpan<byte> HttpSlashPrefix => "HTTP/"u8;
    private static readonly Encoding Iso88591 = Encoding.GetEncoding("iso-8859-1");

    private readonly int _maxHeaderSize;
    private readonly int _maxTotalHeaderSize;

    private ReadOnlyMemory<byte> _remainder = ReadOnlyMemory<byte>.Empty;
    private IMemoryOwner<byte>? _remainderOwner;
    private int _remainderLength;  // Actual data length (MemoryPool may allocate more)
    private bool _isHttp09;
    private int? _pendingContentLength;  // Non-null if waiting for body data due to Content-Length

    /// <summary>
    /// Returns true if the decoder is waiting for body data due to a Content-Length header.
    /// This is used to detect Content-Length mismatches when the connection abruptly closes.
    /// </summary>
    public bool IsWaitingForContentLength => _pendingContentLength.HasValue;

    public Decoder(int maxHeaderSize = DefaultMaxHeaderSize, int maxTotalHeaderSize = DefaultMaxTotalHeaderSize)
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
                if (!HttpSlashPrefix[..span.Length].SequenceEqual(span))
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

        var lines = SplitHeaderLines(working.Span[..headerEnd]);
        if (lines.Length == 0)
        {
            return false;
        }

        StatusLineDecoder.Validate(lines[0]);
        var headers = HeaderDecoder.Parse(lines[1..], _maxHeaderSize, _maxTotalHeaderSize);
        var bodyStart = headerEnd + GetHeaderDelimiterLength(working.Span, headerEnd);
        var bodyData = working[bodyStart..];

        var statusCode = StatusLineDecoder.ParseCode(lines[0]);

        // No-body responses: 204 and 304 always have empty body (RFC 1945 §7)
        if (statusCode is 204 or 304)
        {
            response = ResponseBuilder.Build(lines[0], headers, []);
            _pendingContentLength = null;
            ReturnRentedBuffer();
            return true;
        }

        var contentLength = HeaderDecoder.ExtractContentLength(headers);
        if (contentLength.HasValue)
        {
            if (bodyData.Length < contentLength.Value)
            {
                _remainder = working;
                _pendingContentLength = contentLength.Value;
                return false;
            }

            response = ResponseBuilder.Build(lines[0], headers, bodyData.Span[..contentLength.Value]);
            _pendingContentLength = null;
            ReturnRentedBuffer();
            return true;
        }

        response = ResponseBuilder.Build(lines[0], headers, bodyData.Span);
        _pendingContentLength = null;
        ReturnRentedBuffer();
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
                response = ResponseBuilder.BuildHttp09([]);
                _isHttp09 = false;
                _pendingContentLength = null;
                ReturnRentedBuffer();
                return true;
            }

            return false;
        }

        // HTTP/0.9: entire remainder is body
        if (_isHttp09)
        {
            response = ResponseBuilder.BuildHttp09(_remainder.Span);
            _remainder = ReadOnlyMemory<byte>.Empty;
            _isHttp09 = false;
            _pendingContentLength = null;
            ReturnRentedBuffer();
            return true;
        }

        var span = _remainder.Span;
        var headerEnd = FindHeaderEnd(span);
        if (headerEnd < 0)
        {
            return false;
        }

        var lines = SplitHeaderLines(span[..headerEnd]);
        if (lines.Length == 0)
        {
            return false;
        }

        StatusLineDecoder.Validate(lines[0]);
        var headers = HeaderDecoder.Parse(lines[1..], _maxHeaderSize, _maxTotalHeaderSize);
        var index = headerEnd + GetHeaderDelimiterLength(span, headerEnd);
        var bodySpan = span[index..];

        // RFC 1945: If a Content-Length was declared but EOF arrived after receiving partial
        // body data, it's a truncation error. When body is empty, allow it — connection
        // may have been closed cleanly after headers (e.g. HEAD response, 204, or abrupt
        // close already detected via CloseSignalItem.AbruptClose before reaching here).
        var contentLength = HeaderDecoder.ExtractContentLength(headers);
        if (contentLength.HasValue && bodySpan.Length > 0 && bodySpan.Length < contentLength.Value)
        {
            throw new HttpDecoderException(HttpDecoderError.InvalidContentLength,
                $"Content-Length mismatch: expected {contentLength.Value} bytes but received {bodySpan.Length} bytes before EOF.");
        }

        response = ResponseBuilder.Build(lines[0], headers, bodySpan);
        _remainder = ReadOnlyMemory<byte>.Empty;
        ReturnRentedBuffer();
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

        var lines = SplitHeaderLines(working.Span[..headerEnd]);
        if (lines.Length == 0)
        {
            return false;
        }

        StatusLineDecoder.Validate(lines[0]);
        var headers = HeaderDecoder.Parse(lines[1..], _maxHeaderSize, _maxTotalHeaderSize);
        var bodyStart = headerEnd + GetHeaderDelimiterLength(working.Span, headerEnd);
        var bodyData = working[bodyStart..];

        var statusCode = StatusLineDecoder.ParseCode(lines[0]);

        // CONNECT 2xx: body length = 0 (tunnel begins)
        if (statusCode is >= 200 and < 300)
        {
            response = ResponseBuilder.Build(lines[0], headers, []);
            _pendingContentLength = null;
            ReturnRentedBuffer();
            return true;
        }

        // Non-2xx: normal body handling (same as TryDecode)
        if (statusCode is 204 or 304)
        {
            response = ResponseBuilder.Build(lines[0], headers, []);
            _pendingContentLength = null;
            ReturnRentedBuffer();
            return true;
        }

        var contentLength = HeaderDecoder.ExtractContentLength(headers);
        if (contentLength.HasValue)
        {
            if (bodyData.Length < contentLength.Value)
            {
                _remainder = working;
                _pendingContentLength = contentLength.Value;
                return false;
            }

            response = ResponseBuilder.Build(lines[0], headers, bodyData.Span[..contentLength.Value]);
            _pendingContentLength = null;
            ReturnRentedBuffer();
            return true;
        }

        response = ResponseBuilder.Build(lines[0], headers, bodyData.Span);
        _pendingContentLength = null;
        ReturnRentedBuffer();
        return true;
    }

    public void Reset()
    {
        ReturnRentedBuffer();
        _remainder = ReadOnlyMemory<byte>.Empty;
        _isHttp09 = false;
        _pendingContentLength = null;
    }

    private void ReturnRentedBuffer()
    {
        if (_remainderOwner != null)
        {
            _remainderOwner.Dispose();
            _remainderOwner = null;
            _remainderLength = 0;
        }
    }

    private ReadOnlyMemory<byte> Combine(ReadOnlyMemory<byte> a, ReadOnlyMemory<byte> b)
    {
        if (a.IsEmpty)
        {
            return b;
        }

        if (b.IsEmpty)
        {
            return a;
        }

        var oldOwner = _remainderOwner;
        var size = a.Length + b.Length;
        var mergedOwner = MemoryPool<byte>.Shared.Rent(size);
        _remainderOwner = mergedOwner;
        _remainderLength = size;
        a.Span.CopyTo(mergedOwner.Memory.Span);
        b.Span.CopyTo(mergedOwner.Memory.Span[a.Length..]);

        oldOwner?.Dispose();

        return mergedOwner.Memory[..size];
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

    private static string[] SplitHeaderLines(ReadOnlySpan<byte> headerBytes)
    {
        var headerText = Iso88591.GetString(headerBytes);
        return headerText.Split(["\r\n", "\n"], StringSplitOptions.None);
    }
}
