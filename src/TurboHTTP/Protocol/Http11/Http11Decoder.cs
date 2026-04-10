using System.Buffers;
using System.Net;
using System.Text;
using TurboHTTP.Internal;
using TurboHTTP.Streams.Stages;

namespace TurboHTTP.Protocol.Http11;

/// <summary>
/// RFC 9112 compliant HTTP/1.1 response decoder with zero-allocation patterns.
/// Uses MemoryPool for buffer management to minimize GC pressure.
/// </summary>
public sealed class Http11Decoder : IDisposable
{
    private IMemoryOwner<byte>? _remainderOwner;
    private int _remainderLength;

    private IMemoryOwner<byte>? _bodyOwner;
    private int _bodyLength;

    private bool _disposed;

    private readonly List<HttpResponseMessage> _decodeBuffer = [];

    // Pooled structures for header parsing — reused across calls (decoder is single-threaded per actor).
    // Avoids ~1 Dictionary + ~15 List<string> allocations per response (~1.6 KB GC pressure per decode).
    private readonly Dictionary<string, List<string>> _headerDict =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Stack<List<string>> _listPool = new();

    private const int DefaultMaxHeaderSize = 16 * 1024; // 16 KB per header field
    private const int DefaultMaxTotalHeaderSize = 64 * 1024; // 64 KB total headers
    private const int DefaultMaxBodySize = 10_485_760; // 10 MB
    private const int DefaultMaxHeaderCount = 100;

    private readonly int _maxHeaderSize;
    private readonly int _maxTotalHeaderSize;
    private readonly int _maxBodySize;
    private readonly int _maxHeaderCount;

    /// <summary>
    /// Creates a new HTTP/1.1 decoder with configurable limits.
    /// </summary>
    /// <param name="maxHeaderSize">Maximum single header field size in bytes (default: 16KB)</param>
    /// <param name="maxTotalHeaderSize">Maximum total header size in bytes (default: 64KB)</param>
    /// <param name="maxBodySize">Maximum body size in bytes (default: 10MB)</param>
    /// <param name="maxHeaderCount">Maximum number of header fields allowed (default: 100)</param>
    public Http11Decoder(
        int maxHeaderSize = DefaultMaxHeaderSize,
        int maxTotalHeaderSize = DefaultMaxTotalHeaderSize,
        int maxBodySize = DefaultMaxBodySize,
        int maxHeaderCount = DefaultMaxHeaderCount)
    {
        _maxHeaderSize = maxHeaderSize;
        _maxTotalHeaderSize = maxTotalHeaderSize;
        _maxBodySize = maxBodySize;
        _maxHeaderCount = maxHeaderCount;
    }

    /// <summary>
    /// Attempts to decode HTTP/1.1 responses from incoming data.
    /// </summary>
    /// <param name="incomingData">New data received from the network</param>
    /// <param name="responses">Decoded responses (may contain multiple for pipelining)</param>
    /// <returns>True if at least one response was decoded</returns>
    public bool TryDecode(ReadOnlyMemory<byte> incomingData, out IReadOnlyList<HttpResponseMessage> responses)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _decodeBuffer.Clear();
        responses = [];

        // Combine remainder with incoming data using pooled buffer
        ReadOnlySpan<byte> working;
        IMemoryOwner<byte>? combinedOwner = null;

        if (_remainderLength > 0)
        {
            var combinedLength = _remainderLength + incomingData.Length;
            combinedOwner = MemoryPool<byte>.Shared.Rent(combinedLength);

            _remainderOwner!.Memory.Span[.._remainderLength].CopyTo(combinedOwner.Memory.Span);
            incomingData.Span.CopyTo(combinedOwner.Memory.Span[_remainderLength..]);

            working = combinedOwner.Memory.Span[..combinedLength];
            ClearRemainder();
        }
        else
        {
            working = incomingData.Span;
        }

        try
        {
            var consumed = 0;

            while (consumed < working.Length)
            {
                var result = TryParseOne(working[consumed..], out var response, out var bytesConsumed);

                if (result.Success)
                {
                    consumed += bytesConsumed;

                    // Skip 1xx informational responses (RFC 9112 Section 4)
                    if ((int)response!.StatusCode >= 100 && (int)response.StatusCode < 200)
                    {
                        continue;
                    }

                    _decodeBuffer.Add(response);
                    continue;
                }

                if (result.Error == HttpDecoderError.NeedMoreData)
                {
                    // Store remainder in pooled buffer
                    StoreRemainder(working[consumed..]);
                    break;
                }

                ClearRemainder();
                throw new HttpDecoderException(result.Error!.Value);
            }
        }
        finally
        {
            combinedOwner?.Dispose();
        }

        if (_decodeBuffer.Count <= 0)
        {
            return false;
        }

        responses = new List<HttpResponseMessage>(_decodeBuffer);
        return true;
    }

    /// <summary>
    /// Attempts to decode HTTP/1.1 responses from incoming data where the original
    /// request was a HEAD request. Parses headers only and always returns an empty body,
    /// regardless of any <c>Content-Length</c> value in the response headers.
    /// </summary>
    /// <remarks>
    /// RFC 9112 §6.3: Any response to a HEAD request is terminated by the first empty
    /// line after the header fields and cannot contain a message body.
    /// </remarks>
    public bool TryDecodeHead(ReadOnlyMemory<byte> incomingData, out IReadOnlyList<HttpResponseMessage> responses)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _decodeBuffer.Clear();
        responses = [];

        ReadOnlySpan<byte> working;
        IMemoryOwner<byte>? combinedOwner = null;

        if (_remainderLength > 0)
        {
            var combinedLength = _remainderLength + incomingData.Length;
            combinedOwner = MemoryPool<byte>.Shared.Rent(combinedLength);

            _remainderOwner!.Memory.Span[.._remainderLength].CopyTo(combinedOwner.Memory.Span);
            incomingData.Span.CopyTo(combinedOwner.Memory.Span[_remainderLength..]);

            working = combinedOwner.Memory.Span[..combinedLength];
            ClearRemainder();
        }
        else
        {
            working = incomingData.Span;
        }

        try
        {
            var consumed = 0;

            while (consumed < working.Length)
            {
                var result = TryParseOneNoBody(working[consumed..], out var response, out var bytesConsumed);

                if (result.Success)
                {
                    consumed += bytesConsumed;

                    if ((int)response!.StatusCode >= 100 && (int)response.StatusCode < 200)
                    {
                        continue;
                    }

                    _decodeBuffer.Add(response);
                    continue;
                }

                if (result.Error == HttpDecoderError.NeedMoreData)
                {
                    StoreRemainder(working[consumed..]);
                    break;
                }

                ClearRemainder();
                throw new HttpDecoderException(result.Error!.Value);
            }
        }
        finally
        {
            combinedOwner?.Dispose();
        }

        if (_decodeBuffer.Count <= 0)
        {
            return false;
        }

        responses = new List<HttpResponseMessage>(_decodeBuffer);
        return true;
    }

    /// <summary>
    /// Attempts to decode HTTP/1.1 responses from incoming data where the original
    /// request was a CONNECT request. A successful (2xx) CONNECT response has no body
    /// (the connection transitions to a tunnel), regardless of any Content-Length or
    /// Transfer-Encoding headers. Non-2xx responses are decoded with normal body handling.
    /// </summary>
    /// <remarks>
    /// RFC 9110 §9.3.6: A server MUST NOT send Content-Length or Transfer-Encoding
    /// in a 2xx (Successful) response to CONNECT. A client MUST ignore any such
    /// header fields received in a successful CONNECT response.
    /// </remarks>
    public bool TryDecodeConnect(ReadOnlyMemory<byte> incomingData, out IReadOnlyList<HttpResponseMessage> responses)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _decodeBuffer.Clear();
        responses = [];

        ReadOnlySpan<byte> working;
        IMemoryOwner<byte>? combinedOwner = null;

        if (_remainderLength > 0)
        {
            var combinedLength = _remainderLength + incomingData.Length;
            combinedOwner = MemoryPool<byte>.Shared.Rent(combinedLength);

            _remainderOwner!.Memory.Span[.._remainderLength].CopyTo(combinedOwner.Memory.Span);
            incomingData.Span.CopyTo(combinedOwner.Memory.Span[_remainderLength..]);

            working = combinedOwner.Memory.Span[..combinedLength];
            ClearRemainder();
        }
        else
        {
            working = incomingData.Span;
        }

        try
        {
            var consumed = 0;

            while (consumed < working.Length)
            {
                // Peek at status code to decide parsing strategy
                var slice = working[consumed..];
                var statusCode = PeekStatusCode(slice);

                // 2xx → no body (tunnel begins); non-2xx → normal body handling
                var result = statusCode is >= 200 and < 300
                    ? TryParseOneNoBody(slice, out var response, out var bytesConsumed)
                    : TryParseOne(slice, out response, out bytesConsumed);

                if (result.Success)
                {
                    consumed += bytesConsumed;

                    if ((int)response!.StatusCode >= 100 && (int)response.StatusCode < 200)
                    {
                        continue;
                    }

                    _decodeBuffer.Add(response);
                    continue;
                }

                if (result.Error == HttpDecoderError.NeedMoreData)
                {
                    StoreRemainder(working[consumed..]);
                    break;
                }

                ClearRemainder();
                throw new HttpDecoderException(result.Error!.Value);
            }
        }
        finally
        {
            combinedOwner?.Dispose();
        }

        if (_decodeBuffer.Count <= 0)
        {
            return false;
        }

        responses = new List<HttpResponseMessage>(_decodeBuffer);
        return true;
    }

    /// <summary>
    /// Attempts to complete a partially buffered response when the connection has closed cleanly.
    /// Called when a TLS close_notify or TCP FIN is received and the server used
    /// connection-close framing (no Content-Length, no Transfer-Encoding).
    /// </summary>
    /// <remarks>
    /// RFC 9112 §9.8: A server MAY close the connection at the end of a response when
    /// the response does not include Content-Length or Transfer-Encoding.
    /// The entire remainder after the header section is treated as the message body.
    /// </remarks>
    /// <param name="response">The completed response, or null if no valid header section was buffered.</param>
    /// <returns>True if a complete response was assembled from the remainder buffer.</returns>
    public bool TryDecodeEof(out HttpResponseMessage? response)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        response = null;
        if (_remainderLength == 0)
        {
            return false;
        }

        var working = _remainderOwner!.Memory.Span[.._remainderLength];

        // Find header/body boundary (CRLF CRLF)
        var headerEnd = FindCrlfCrlf(working);
        if (headerEnd < 0)
        {
            return false;
        }

        // Include the CRLF that terminates the last header
        var headerSection = working[..(headerEnd + 2)];

        // Parse status line
        var statusLineEnd = FindCrlf(headerSection, 0);
        if (statusLineEnd < 0)
        {
            return false;
        }

        var statusLine = headerSection[..statusLineEnd];
        if (!TryParseStatusLine(statusLine, out var statusCode, out var reasonPhrase))
        {
            return false;
        }

        // Parse headers
        var headersData = headerSection[(statusLineEnd + 2)..];
        var headers = ParseHeaders(headersData);

        // RFC 9112 §7.1: Chunked encoding MUST be terminated by a zero-length chunk.
        // If the connection closes before the zero-chunk, the response is incomplete.
        if (GetSingleHeader(headers, WellKnownHeaders.Names.TransferEncoding) is not null)
        {
            ClearRemainder();
            return false;
        }

        var bodyStart = headerEnd + 4;
        var bodySpan = bodyStart < _remainderLength
            ? working[bodyStart..]
            : ReadOnlySpan<byte>.Empty;

        // RFC 9112 §6.2: If Content-Length is present, the body MUST be exactly that many bytes.
        // A connection close before the full body is received means a truncated (incomplete) response.
        var contentLength = GetContentLengthHeader(headers);
        if (contentLength.HasValue && bodySpan.Length < contentLength.Value)
        {
            ClearRemainder();
            return false;
        }

        // Build response
        response = new HttpResponseMessage
        {
            StatusCode = (HttpStatusCode)statusCode,
            ReasonPhrase = reasonPhrase,
            Version = HttpVersion.Version11
        };

        foreach (var (name, values) in headers)
        {
            foreach (var value in values)
            {
                response.Headers.TryAddWithoutValidation(name, value);
            }
        }

        // Use pooled memory instead of byte[] allocation
        HttpContent content;
        if (!bodySpan.IsEmpty)
        {
            var owner = MemoryPool<byte>.Shared.Rent(bodySpan.Length);
            bodySpan.CopyTo(owner.Memory.Span);
            content = new PooledBodyContent(owner, bodySpan.Length);
        }
        else
        {
            content = new ByteArrayContent([]);
        }

        foreach (var (name, values) in headers)
        {
            if (!IsContentHeader(name))
            {
                continue;
            }

            foreach (var value in values)
            {
                content.Headers.TryAddWithoutValidation(name, value);
            }
        }

        response.Content = content;
        ClearRemainder();
        return true;
    }

    /// <summary>
    /// Returns any buffered remainder bytes and clears the remainder.
    /// Used by <see cref="Http11ConnectionStage"/> to extract
    /// body data that was in the same chunk as headers for connection-close-delimited responses.
    /// </summary>
    public byte[] FlushRemainder()
    {
        if (_remainderLength == 0)
        {
            return [];
        }

        var result = new byte[_remainderLength];
        _remainderOwner!.Memory.Span[.._remainderLength].CopyTo(result);
        ClearRemainder();
        return result;
    }

    /// <summary>
    /// Resets decoder state for reuse on a new connection.
    /// </summary>
    public void Reset()
    {
        ClearRemainder();
        ClearBody();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _remainderOwner?.Dispose();
        _remainderOwner = null;

        _bodyOwner?.Dispose();
        _bodyOwner = null;
    }

    private void StoreRemainder(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
        {
            return;
        }

        if (_remainderOwner == null || _remainderOwner.Memory.Length < data.Length)
        {
            _remainderOwner?.Dispose();
            _remainderOwner = MemoryPool<byte>.Shared.Rent(data.Length);
        }

        data.CopyTo(_remainderOwner.Memory.Span);
        _remainderLength = data.Length;
    }

    private void ClearRemainder()
    {
        _remainderLength = 0;
        // Keep buffer for reuse
    }

    private void ClearBody()
    {
        _bodyLength = 0;
        // Keep buffer for reuse
    }

    private void EnsureBodyCapacity(int required)
    {
        if (_bodyOwner != null && _bodyOwner.Memory.Length >= required)
        {
            return;
        }

        var newOwner = MemoryPool<byte>.Shared.Rent(required);
        if (_bodyOwner != null)
        {
            _bodyOwner.Memory.Span[.._bodyLength].CopyTo(newOwner.Memory.Span);
            _bodyOwner.Dispose();
        }

        _bodyOwner = newOwner;
    }

    /// <summary>
    /// Parses one response but always returns an empty body (used for HEAD responses).
    /// </summary>
    private HttpDecodeResult TryParseOneNoBody(ReadOnlySpan<byte> buffer, out HttpResponseMessage? response,
        out int consumed)
    {
        response = null;
        consumed = 0;

        var headerEnd = FindCrlfCrlf(buffer);
        if (headerEnd < 0)
        {
            return HttpDecodeResult.Incomplete();
        }

        // Early reject: total header section (including status line) exceeds total limit.
        if (headerEnd > _maxTotalHeaderSize)
        {
            return HttpDecodeResult.Fail(HttpDecoderError.TotalHeadersTooLarge);
        }

        var headerSection = buffer[..(headerEnd + 2)];

        var statusLineEnd = FindCrlf(headerSection, 0);
        if (statusLineEnd < 0)
        {
            return HttpDecodeResult.Fail(HttpDecoderError.InvalidStatusLine);
        }

        var statusLine = headerSection[..statusLineEnd];
        if (!TryParseStatusLine(statusLine, out var statusCode, out var reasonPhrase))
        {
            return HttpDecodeResult.Fail(HttpDecoderError.InvalidStatusLine);
        }

        var headersData = headerSection[(statusLineEnd + 2)..];
        var headers = ParseHeaders(headersData);

        response = new HttpResponseMessage
        {
            StatusCode = (HttpStatusCode)statusCode,
            ReasonPhrase = reasonPhrase,
            Version = HttpVersion.Version11
        };

        foreach (var (name, values) in headers)
        {
            foreach (var value in values)
            {
                response.Headers.TryAddWithoutValidation(name, value);
            }
        }

        // Always return empty body for HEAD responses (RFC 9112 §6.3)
        var emptyContent = new ByteArrayContent([]);
        foreach (var (name, values) in headers)
        {
            if (!IsContentHeader(name))
            {
                continue;
            }

            foreach (var value in values)
            {
                emptyContent.Headers.TryAddWithoutValidation(name, value);
            }
        }

        response.Content = emptyContent;
        consumed = headerEnd + 4; // Skip past \r\n\r\n
        return HttpDecodeResult.Ok();
    }

    private HttpDecodeResult TryParseOne(ReadOnlySpan<byte> buffer, out HttpResponseMessage? response, out int consumed)
    {
        response = null;
        consumed = 0;

        // 1. Find header/body boundary (CRLF CRLF)
        var headerEnd = FindCrlfCrlf(buffer);
        if (headerEnd < 0)
        {
            return HttpDecodeResult.Incomplete();
        }

        // Early reject: total header section (including status line) exceeds total limit.
        if (headerEnd > _maxTotalHeaderSize)
        {
            return HttpDecodeResult.Fail(HttpDecoderError.TotalHeadersTooLarge);
        }

        // Include the CRLF that terminates the last header so FindCrlf/ParseHeaders work correctly.
        var headerSection = buffer[..(headerEnd + 2)];

        // 2. Parse status line (RFC 9112 Section 4)
        var statusLineEnd = FindCrlf(headerSection, 0);
        if (statusLineEnd < 0)
        {
            return HttpDecodeResult.Fail(HttpDecoderError.InvalidStatusLine);
        }

        var statusLine = headerSection[..statusLineEnd];
        if (!TryParseStatusLine(statusLine, out var statusCode, out var reasonPhrase))
        {
            return HttpDecodeResult.Fail(HttpDecoderError.InvalidStatusLine);
        }

        // 3. Parse headers using span-based parsing
        var headersData = headerSection[(statusLineEnd + 2)..];
        var headers = ParseHeaders(headersData);

        // 4. Build response object
        response = new HttpResponseMessage
        {
            StatusCode = (HttpStatusCode)statusCode,
            ReasonPhrase = reasonPhrase,
            Version = HttpVersion.Version11
        };

        foreach (var (name, values) in headers)
        {
            foreach (var value in values)
            {
                response.Headers.TryAddWithoutValidation(name, value);
            }
        }

        var bodyStart = headerEnd + 4;
        var bodyData = buffer[bodyStart..];

        // 5. Handle no-body responses (RFC 9112 Section 6.3)
        if (IsNoBodyResponse(statusCode))
        {
            var emptyContent = new ByteArrayContent([]);
            foreach (var (name, values) in headers)
            {
                if (!IsContentHeader(name))
                {
                    continue;
                }

                foreach (var value in values)
                {
                    emptyContent.Headers.TryAddWithoutValidation(name, value);
                }
            }

            response.Content = emptyContent;
            consumed = bodyStart;
            return HttpDecodeResult.Ok();
        }

        // 6. Parse body
        var (bodyResult, bodyOwner, bodyLength, bodyConsumed, trailerHeaders) = ParseBody(bodyData, headers);
        if (!bodyResult.Success)
        {
            return bodyResult;
        }

        // 7. Create content — use pooled memory to avoid byte[] allocation
        HttpContent content = bodyOwner is not null
            ? new PooledBodyContent(bodyOwner, bodyLength)
            : new ByteArrayContent([]);

        foreach (var (name, values) in headers)
        {
            if (!IsContentHeader(name))
            {
                continue;
            }

            foreach (var value in values)
            {
                content.Headers.TryAddWithoutValidation(name, value);
            }
        }

        // 8. Add trailer headers
        if (trailerHeaders != null)
        {
            foreach (var (name, values) in trailerHeaders)
            {
                foreach (var value in values)
                {
                    response.TrailingHeaders.TryAddWithoutValidation(name, value);
                }
            }
        }

        response.Content = content;
        consumed = bodyStart + bodyConsumed;
        return HttpDecodeResult.Ok();
    }

    private static string GetOrCreateReasonPhrase(ReadOnlySpan<byte> span)
        => span.Length switch
        {
            2  => span.SequenceEqual("OK"u8)                    ? "OK"                    : Encoding.ASCII.GetString(span),
            5  => span.SequenceEqual("Found"u8)                 ? "Found"                 : Encoding.ASCII.GetString(span),
            7  => span.SequenceEqual("Created"u8)               ? "Created"               : Encoding.ASCII.GetString(span),
            8  => span.SequenceEqual("Accepted"u8)              ? "Accepted"              : Encoding.ASCII.GetString(span),
            9  => span.SequenceEqual("Not Found"u8)             ? "Not Found"             :
                  span.SequenceEqual("Forbidden"u8)             ? "Forbidden"             : Encoding.ASCII.GetString(span),
            10 => span.SequenceEqual("No Content"u8)            ? "No Content"            : Encoding.ASCII.GetString(span),
            11 => span.SequenceEqual("Bad Request"u8)           ? "Bad Request"           : Encoding.ASCII.GetString(span),
            12 => span.SequenceEqual("Unauthorized"u8)          ? "Unauthorized"          :
                  span.SequenceEqual("Not Modified"u8)          ? "Not Modified"          : Encoding.ASCII.GetString(span),
            15 => span.SequenceEqual("Partial Content"u8)       ? "Partial Content"       : Encoding.ASCII.GetString(span),
            17 => span.SequenceEqual("Moved Permanently"u8)     ? "Moved Permanently"     : Encoding.ASCII.GetString(span),
            21 => span.SequenceEqual("Internal Server Error"u8) ? "Internal Server Error" : Encoding.ASCII.GetString(span),
            _  => Encoding.ASCII.GetString(span),
        };

    private static bool TryParseStatusLine(ReadOnlySpan<byte> line, out int statusCode, out string reasonPhrase)
    {
        statusCode = 0;
        reasonPhrase = string.Empty;

        // Format: HTTP/1.1 200 OK
        // Minimum: "HTTP/1.1 200" = 12 chars
        if (line.Length < 12)
        {
            return false;
        }

        // Check HTTP version prefix
        if (!line.StartsWith("HTTP/1."u8))
        {
            return false;
        }

        // Find first space after version
        var firstSpace = line.IndexOf((byte)' ');
        if (firstSpace < 8)
        {
            return false;
        }

        // Parse status code (3 digits)
        var codeStart = firstSpace + 1;
        if (codeStart + 3 > line.Length)
        {
            return false;
        }

        var codeSpan = line.Slice(codeStart, 3);
        if (!TryParseInt(codeSpan, out statusCode))
        {
            return false;
        }

        // Parse reason phrase (optional)
        var reasonStart = codeStart + 4; // "200 "
        if (reasonStart < line.Length)
        {
            reasonPhrase = GetOrCreateReasonPhrase(line[reasonStart..]);
        }

        return statusCode is >= 100 and < 600;
    }

    private Dictionary<string, List<string>> ParseHeaders(ReadOnlySpan<byte> data)
    {
        // Return List<string> instances from the previous call to the pool before clearing.
        foreach (var list in _headerDict.Values)
        {
            list.Clear();
            if (_listPool.Count < 32)
            {
                _listPool.Push(list);
            }
        }

        _headerDict.Clear();

        var pos = 0;
        var fieldCount = 0;
        var totalSize = 0;

        while (pos < data.Length)
        {
            var lineEnd = FindCrlf(data, pos);
            if (lineEnd < 0 || lineEnd == pos)
            {
                break;
            }

            // Security: enforce maximum header field count (prevents header flood attacks).
            fieldCount++;
            if (fieldCount > _maxHeaderCount)
            {
                throw new HttpDecoderException(HttpDecoderError.TooManyHeaders,
                    $"Received {fieldCount} fields; limit is {_maxHeaderCount}.");
            }

            var line = data[pos..lineEnd];

            // RFC 9112 §5.2: obs-fold (continuation line starting with SP/HT) is obsolete.
            // Reject it as InvalidHeader — the line has no colon so colonIdx check below handles it.
            // However, explicitly detect it here for clarity and correct size accounting.
            if (line.Length > 0 && (line[0] == (byte)' ' || line[0] == (byte)'\t'))
            {
                throw new HttpDecoderException(HttpDecoderError.ObsoleteFoldingDetected);
            }

            var colonIdx = line.IndexOf((byte)':');

            // RFC 9112 §5.1: every header field MUST contain a colon.
            // colonIdx == -1: no colon present; colonIdx == 0: empty field name — both are invalid.
            if (colonIdx <= 0)
            {
                throw new HttpDecoderException(HttpDecoderError.InvalidHeader);
            }

            var name = WellKnownHeaders.TrimOws(line[..colonIdx]);
            var value = WellKnownHeaders.TrimOws(line[(colonIdx + 1)..]);

            var nameStr = WellKnownHeaders.GetOrCreateHeaderName(name);
            var valueStr = WellKnownHeaders.GetOrCreateHeaderValue(value);

            // RFC 9112 §5.5: Header field values MUST NOT contain CR, LF, or NUL characters.
            if (valueStr.IndexOfAny('\r', '\n', '\0') >= 0)
            {
                throw new HttpDecoderException(HttpDecoderError.InvalidFieldValue,
                    $"Header '{nameStr}' contains a CR, LF, or NUL character in its value.");
            }

            // Security: check single header field size (name + ": " + value).
            var headerSize = name.Length + 2 + value.Length;
            if (headerSize > _maxHeaderSize)
            {
                throw new HttpDecoderException(HttpDecoderError.HeaderTooLarge,
                    $"Header '{nameStr}' is {headerSize} bytes; limit is {_maxHeaderSize}.");
            }

            // Security: check cumulative total header size.
            totalSize += headerSize;
            if (totalSize > _maxTotalHeaderSize)
            {
                throw new HttpDecoderException(HttpDecoderError.TotalHeadersTooLarge,
                    $"Total header size is {totalSize} bytes; limit is {_maxTotalHeaderSize}.");
            }

            if (!_headerDict.TryGetValue(nameStr, out var values))
            {
                values = _listPool.Count > 0 ? _listPool.Pop() : new List<string>(2);
                _headerDict[nameStr] = values;
            }

            values.Add(valueStr);

            pos = lineEnd + 2;
        }

        return _headerDict;
    }

    private (HttpDecodeResult result, IMemoryOwner<byte>? bodyOwner, int bodyLength, int consumed, Dictionary<string, List<string>>? trailers)
        ParseBody(ReadOnlySpan<byte> data, Dictionary<string, List<string>> headers)
    {
        var transferEncoding = GetSingleHeader(headers, WellKnownHeaders.Names.TransferEncoding);
        var contentLength = GetContentLengthHeader(headers);

        // RFC 9112 Section 6.3: Transfer-Encoding takes precedence
        if (!string.IsNullOrEmpty(transferEncoding) &&
            transferEncoding.Contains("chunked", StringComparison.OrdinalIgnoreCase))
        {
            // RFC 9112 §6.3 / Security: Reject responses with both Transfer-Encoding and Content-Length
            // to prevent HTTP request smuggling attacks.
            if (contentLength.HasValue)
            {
                return (HttpDecodeResult.Fail(HttpDecoderError.ChunkedWithContentLength), null, 0, 0, null);
            }

            return ParseChunkedBody(data);
        }

        if (!contentLength.HasValue)
        {
            return (HttpDecodeResult.Ok(), null, 0, 0, null);
        }

        var len = contentLength.Value;

        if (len > _maxBodySize)
        {
            return (HttpDecodeResult.Fail(HttpDecoderError.InvalidContentLength), null, 0, 0, null);
        }

        if (data.Length < len)
        {
            return (HttpDecodeResult.Incomplete(), null, 0, 0, null);
        }

        // Rent from MemoryPool instead of allocating a new byte[] via ToArray()
        var owner = MemoryPool<byte>.Shared.Rent(len);
        data[..len].CopyTo(owner.Memory.Span);
        return (HttpDecodeResult.Ok(), owner, len, len, null);

        // No Content-Length and no Transfer-Encoding: empty body
    }

    private (HttpDecodeResult result, IMemoryOwner<byte>? bodyOwner, int bodyLength, int consumed, Dictionary<string, List<string>>? trailers)
        ParseChunkedBody(ReadOnlySpan<byte> data)
    {
        ClearBody();
        var pos = 0;

        while (pos < data.Length)
        {
            // Find chunk size line end
            var lineEnd = FindCrlf(data, pos);
            if (lineEnd < 0)
            {
                return (HttpDecodeResult.Incomplete(), null, 0, 0, null);
            }

            // Parse chunk size (hex) and optional chunk extensions (RFC 9112 §7.1.1)
            var sizeLine = data[pos..lineEnd];
            var semiIdx = sizeLine.IndexOf((byte)';');
            var sizeSpan = semiIdx >= 0 ? sizeLine[..semiIdx] : sizeLine;
            var extSpan = semiIdx >= 0 ? sizeLine[(semiIdx + 1)..] : ReadOnlySpan<byte>.Empty;

            if (!TryParseChunkExtensions(extSpan))
            {
                return (HttpDecodeResult.Fail(HttpDecoderError.InvalidChunkExtension), null, 0, 0, null);
            }

            if (!TryParseHex(sizeSpan, out var chunkSize))
            {
                return (HttpDecodeResult.Fail(HttpDecoderError.InvalidChunkSize), null, 0, 0, null);
            }

            pos = lineEnd + 2;

            // Last chunk (size = 0)
            if (chunkSize == 0)
            {
                var remaining = data[pos..];

                // Empty trailer section: just a CRLF terminator
                if (remaining.Length >= 2 && remaining[0] == '\r' && remaining[1] == '\n')
                {
                    var (owner, len) = RentBodyOwner();
                    return (HttpDecodeResult.Ok(), owner, len, pos + 2, null);
                }

                // Trailer headers present: look for the CRLFCRLF terminator
                var trailerEnd = FindCrlfCrlf(remaining);
                if (trailerEnd >= 0)
                {
                    var trailerData = remaining[..(trailerEnd + 2)]; // include final header CRLF
                    var trailers = ParseHeaders(trailerData);

                    var (owner, len) = RentBodyOwner();
                    return (HttpDecodeResult.Ok(), owner, len, pos + trailerEnd + 4, trailers);
                }

                return (HttpDecodeResult.Incomplete(), null, 0, 0, null);
            }

            // Validate chunk size
            if (chunkSize > _maxBodySize || _bodyLength + chunkSize > _maxBodySize)
            {
                return (HttpDecodeResult.Fail(HttpDecoderError.InvalidContentLength), null, 0, 0, null);
            }

            // Need chunk data + CRLF
            if (pos + chunkSize + 2 > data.Length)
            {
                return (HttpDecodeResult.Incomplete(), null, 0, 0, null);
            }

            // Append chunk data to body buffer
            EnsureBodyCapacity(_bodyLength + chunkSize);
            data.Slice(pos, chunkSize).CopyTo(_bodyOwner!.Memory.Span[_bodyLength..]);
            _bodyLength += chunkSize;

            pos += chunkSize + 2; // Skip chunk data and trailing CRLF
        }

        return (HttpDecodeResult.Incomplete(), null, 0, 0, null);
    }

    /// <summary>
    /// Rents a <see cref="IMemoryOwner{T}"/> from <see cref="MemoryPool{T}.Shared"/> and copies
    /// the accumulated chunked body into it. Returns null owner for empty bodies.
    /// </summary>
    private (IMemoryOwner<byte>? owner, int length) RentBodyOwner()
    {
        if (_bodyLength == 0)
        {
            return (null, 0);
        }

        var owner = MemoryPool<byte>.Shared.Rent(_bodyLength);
        _bodyOwner!.Memory.Span[.._bodyLength].CopyTo(owner.Memory.Span);
        return (owner, _bodyLength);
    }

    private static bool IsNoBodyResponse(int statusCode) =>
        statusCode is >= 100 and < 200 or 204 or 304;

    /// <summary>
    /// Peeks at the status code from a raw HTTP response without fully parsing it.
    /// Returns null if the status line is not yet complete.
    /// </summary>
    private static int? PeekStatusCode(ReadOnlySpan<byte> buffer)
    {
        // Status line format: HTTP/1.1 200 OK\r\n
        // Minimum: "HTTP/1.1 NNN" = 12 bytes
        if (buffer.Length < 12)
        {
            return null;
        }

        // Find the space after version (position 8 for "HTTP/1.1 ")
        var spaceIdx = buffer.IndexOf((byte)' ');
        if (spaceIdx < 0 || spaceIdx + 4 > buffer.Length)
        {
            return null;
        }

        var codeSlice = buffer.Slice(spaceIdx + 1, 3);
        if (codeSlice[0] < (byte)'1' || codeSlice[0] > (byte)'5')
        {
            return null;
        }

        var code = (codeSlice[0] - '0') * 100 + (codeSlice[1] - '0') * 10 + (codeSlice[2] - '0');
        return code;
    }

    private static bool IsContentHeader(string name) =>
        name.StartsWith("content-", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("content-length", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("content-type", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("allow", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("expires", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("last-modified", StringComparison.OrdinalIgnoreCase);


    private static int? GetContentLengthHeader(Dictionary<string, List<string>> headers)
    {
        if (!headers.TryGetValue(WellKnownHeaders.Names.ContentLength, out var values) || values.Count == 0)
        {
            return null;
        }

        // RFC 9112 Section 6.3: Multiple Content-Length with different values is error
        if (values.Count > 1)
        {
            var first = values[0];
            for (var i = 1; i < values.Count; i++)
            {
                if (!values[i].Equals(first, StringComparison.Ordinal))
                {
                    throw new HttpDecoderException(
                        HttpDecoderError.MultipleContentLengthValues,
                        $"Values '{first}' and '{values[i]}' conflict.");
                }
            }
        }

        return int.TryParse(values[0], out var len) && len >= 0 ? len : null;
    }

    private static string? GetSingleHeader(Dictionary<string, List<string>> headers, string name) =>
        headers.TryGetValue(name, out var values) && values.Count > 0
            ? values[0]
            : null;

    private static int FindCrlfCrlf(ReadOnlySpan<byte> span)
    {
        for (var i = 0; i <= span.Length - 4; i++)
        {
            if (span[i] == '\r' && span[i + 1] == '\n' &&
                span[i + 2] == '\r' && span[i + 3] == '\n')
            {
                return i;
            }
        }

        return -1;
    }

    private static int FindCrlf(ReadOnlySpan<byte> span, int start)
    {
        for (var i = start; i < span.Length - 1; i++)
        {
            if (span[i] == '\r' && span[i + 1] == '\n')
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// RFC 9112 §7.1.1: Validates chunk-ext syntax.
    /// chunk-ext = *( BWS ";" BWS chunk-ext-name [ BWS "=" BWS chunk-ext-val ] )
    /// Semantics of extensions are ignored; only syntax is validated.
    /// </summary>
    private static bool TryParseChunkExtensions(ReadOnlySpan<byte> extBytes)
    {
        if (extBytes.IsEmpty)
        {
            return true;
        }

        var pos = 0;
        while (pos < extBytes.Length)
        {
            // Skip BWS before name
            while (pos < extBytes.Length && (extBytes[pos] == ' ' || extBytes[pos] == '\t'))
            {
                pos++;
            }

            var nameStart = pos;
            while (pos < extBytes.Length && IsTokenChar(extBytes[pos]) && extBytes[pos] != ';')
            {
                pos++;
            }

            if (pos == nameStart)
            {
                return false;
            }

            // Skip BWS after name
            while (pos < extBytes.Length && (extBytes[pos] == ' ' || extBytes[pos] == '\t'))
            {
                pos++;
            }

            if (pos < extBytes.Length && extBytes[pos] == '=')
            {
                pos++;

                // Skip BWS after '='
                while (pos < extBytes.Length && (extBytes[pos] == ' ' || extBytes[pos] == '\t'))
                {
                    pos++;
                }

                if (pos < extBytes.Length && extBytes[pos] == '"')
                {
                    // Quoted string value
                    pos++;
                    while (pos < extBytes.Length && extBytes[pos] != '"')
                    {
                        if (extBytes[pos] == '\\')
                        {
                            pos += 2;
                        }
                        else
                        {
                            pos++;
                        }
                    }

                    if (pos >= extBytes.Length)
                    {
                        return false;
                    }

                    pos++; // consume closing '"'
                }
                else
                {
                    // Token value
                    var valStart = pos;
                    while (pos < extBytes.Length && IsTokenChar(extBytes[pos]) && extBytes[pos] != ';')
                    {
                        pos++;
                    }

                    if (pos == valStart)
                    {
                        return false;
                    }
                }
            }

            // Skip BWS after value
            while (pos < extBytes.Length && (extBytes[pos] == ' ' || extBytes[pos] == '\t'))
            {
                pos++;
            }

            if (pos < extBytes.Length && extBytes[pos] == ';')
            {
                pos++;
            }
            else if (pos < extBytes.Length)
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsTokenChar(byte b)
    {
        return b switch
        {
            (byte)'!' or (byte)'#' or (byte)'$' or (byte)'%' or (byte)'&' or (byte)'\''
                or (byte)'*' or (byte)'+' or (byte)'-' or (byte)'.' or (byte)'^' or (byte)'_'
                or (byte)'`' or (byte)'|' or (byte)'~' => true,
            _ => b is >= (byte)'0' and <= (byte)'9' or >= (byte)'A' and <= (byte)'Z' or >= (byte)'a' and <= (byte)'z'
        };
    }

    private static bool TryParseInt(ReadOnlySpan<byte> span, out int value)
    {
        value = 0;
        foreach (var b in span)
        {
            if (b < '0' || b > '9')
            {
                return false;
            }

            value = value * 10 + (b - '0');
        }

        return span.Length > 0;
    }

    private static bool TryParseHex(ReadOnlySpan<byte> span, out int value)
    {
        value = 0;
        foreach (var b in span)
        {
            int digit;
            if (b >= '0' && b <= '9')
            {
                digit = b - '0';
            }
            else if (b >= 'a' && b <= 'f')
            {
                digit = b - 'a' + 10;
            }
            else if (b >= 'A' && b <= 'F')
            {
                digit = b - 'A' + 10;
            }
            else
            {
                return false;
            }

            // Detect overflow: if top 4 bits are non-zero, shifting left 4 would overflow int
            if (value >> 28 != 0)
            {
                return false;
            }

            value = (value << 4) | digit;
        }

        return span.Length > 0;
    }
}