using System;
using System.Buffers;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Net;
using System.Net.Http;
using System.Text;

namespace TurboHttp.Protocol;

/// <summary>
/// RFC 9112 compliant HTTP/1.1 response decoder with zero-allocation patterns.
/// Uses ArrayPool for buffer management to minimize GC pressure.
/// </summary>
public sealed class Http11Decoder : IDisposable
{
    // ── Pooled Buffers ──────────────────────────────────────────────────────────

    private byte[]? _remainderBuffer;
    private int _remainderLength;

    private byte[]? _bodyBuffer;
    private int _bodyLength;

    private bool _disposed;

    // ── Configuration ───────────────────────────────────────────────────────────

    private readonly int _maxHeaderSize;
    private readonly int _maxBodySize;
    private readonly int _maxHeaderCount;

    /// <summary>
    /// Creates a new HTTP/1.1 decoder with configurable limits.
    /// </summary>
    /// <param name="maxHeaderSize">Maximum header section size in bytes (default: 8KB)</param>
    /// <param name="maxBodySize">Maximum body size in bytes (default: 10MB)</param>
    /// <param name="maxHeaderCount">Maximum number of header fields allowed (default: 100)</param>
    public Http11Decoder(int maxHeaderSize = 8192, int maxBodySize = 10_485_760, int maxHeaderCount = 100)
    {
        _maxHeaderSize = maxHeaderSize;
        _maxBodySize = maxBodySize;
        _maxHeaderCount = maxHeaderCount;
    }

    // ── Public API ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Attempts to decode HTTP/1.1 responses from incoming data.
    /// </summary>
    /// <param name="incomingData">New data received from the network</param>
    /// <param name="responses">Decoded responses (may contain multiple for pipelining)</param>
    /// <returns>True if at least one response was decoded</returns>
    public bool TryDecode(ReadOnlyMemory<byte> incomingData, out ImmutableList<HttpResponseMessage> responses)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var builder = ImmutableList.CreateBuilder<HttpResponseMessage>();
        responses = ImmutableList<HttpResponseMessage>.Empty;

        // Combine remainder with incoming data using pooled buffer
        ReadOnlySpan<byte> working;
        byte[]? combinedBuffer = null;
        int combinedLength;

        if (_remainderLength > 0)
        {
            combinedLength = _remainderLength + incomingData.Length;
            combinedBuffer = ArrayPool<byte>.Shared.Rent(combinedLength);

            _remainderBuffer.AsSpan(0, _remainderLength).CopyTo(combinedBuffer);
            incomingData.Span.CopyTo(combinedBuffer.AsSpan(_remainderLength));

            working = combinedBuffer.AsSpan(0, combinedLength);
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

                    builder.Add(response);
                    continue;
                }

                if (result.Error == HttpDecodeError.NeedMoreData)
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
            if (combinedBuffer != null)
            {
                ArrayPool<byte>.Shared.Return(combinedBuffer);
            }
        }

        if (builder.Count <= 0) return false;
        responses = builder.ToImmutable();
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
    public bool TryDecodeHead(ReadOnlyMemory<byte> incomingData, out ImmutableList<HttpResponseMessage> responses)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var builder = ImmutableList.CreateBuilder<HttpResponseMessage>();
        responses = ImmutableList<HttpResponseMessage>.Empty;

        ReadOnlySpan<byte> working;
        byte[]? combinedBuffer = null;
        var combinedLength = 0;

        if (_remainderLength > 0)
        {
            combinedLength = _remainderLength + incomingData.Length;
            combinedBuffer = ArrayPool<byte>.Shared.Rent(combinedLength);

            _remainderBuffer.AsSpan(0, _remainderLength).CopyTo(combinedBuffer);
            incomingData.Span.CopyTo(combinedBuffer.AsSpan(_remainderLength));

            working = combinedBuffer.AsSpan(0, combinedLength);
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

                    builder.Add(response);
                    continue;
                }

                if (result.Error == HttpDecodeError.NeedMoreData)
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
            if (combinedBuffer != null)
            {
                ArrayPool<byte>.Shared.Return(combinedBuffer);
            }
        }

        if (builder.Count > 0)
        {
            responses = builder.ToImmutable();
            return true;
        }

        return false;
    }

    /// <summary>
    /// Resets decoder state for reuse on a new connection.
    /// </summary>
    public void Reset()
    {
        ClearRemainder();
        ClearBody();
    }

    // ── IDisposable ─────────────────────────────────────────────────────────────

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        if (_remainderBuffer != null)
        {
            ArrayPool<byte>.Shared.Return(_remainderBuffer);
            _remainderBuffer = null;
        }

        if (_bodyBuffer == null) return;
        ArrayPool<byte>.Shared.Return(_bodyBuffer);
        _bodyBuffer = null;
    }

    // ── Buffer Management ───────────────────────────────────────────────────────

    private void StoreRemainder(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
            return;

        if (_remainderBuffer == null || _remainderBuffer.Length < data.Length)
        {
            if (_remainderBuffer != null)
            {
                ArrayPool<byte>.Shared.Return(_remainderBuffer);
            }

            _remainderBuffer = ArrayPool<byte>.Shared.Rent(data.Length);
        }

        data.CopyTo(_remainderBuffer);
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
        if (_bodyBuffer != null && _bodyBuffer.Length >= required) return;
        var newBuffer = ArrayPool<byte>.Shared.Rent(required);
        if (_bodyBuffer != null)
        {
            _bodyBuffer.AsSpan(0, _bodyLength).CopyTo(newBuffer);
            ArrayPool<byte>.Shared.Return(_bodyBuffer);
        }

        _bodyBuffer = newBuffer;
    }

    // ── Response Parsing ────────────────────────────────────────────────────────

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

        if (headerEnd > _maxHeaderSize)
        {
            return HttpDecodeResult.Fail(HttpDecodeError.LineTooLong);
        }

        var headerSection = buffer[..(headerEnd + 2)];

        var statusLineEnd = FindCrlf(headerSection, 0);
        if (statusLineEnd < 0)
        {
            return HttpDecodeResult.Fail(HttpDecodeError.InvalidStatusLine);
        }

        var statusLine = headerSection[..statusLineEnd];
        if (!TryParseStatusLine(statusLine, out var statusCode, out var reasonPhrase))
        {
            return HttpDecodeResult.Fail(HttpDecodeError.InvalidStatusLine);
        }

        var headersData = headerSection[(statusLineEnd + 2)..];
        var headers = ParseHeaders(headersData);

        response = new HttpResponseMessage
        {
            StatusCode = (HttpStatusCode)statusCode,
            ReasonPhrase = reasonPhrase,
            Version = new Version(1, 1)
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
            if (!IsContentHeader(name)) continue;
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

        // Check header size limit
        if (headerEnd > _maxHeaderSize)
        {
            return HttpDecodeResult.Fail(HttpDecodeError.LineTooLong);
        }

        // Include the CRLF that terminates the last header so FindCrlf/ParseHeaders work correctly.
        var headerSection = buffer[..(headerEnd + 2)];

        // 2. Parse status line (RFC 9112 Section 4)
        var statusLineEnd = FindCrlf(headerSection, 0);
        if (statusLineEnd < 0)
        {
            return HttpDecodeResult.Fail(HttpDecodeError.InvalidStatusLine);
        }

        var statusLine = headerSection[..statusLineEnd];
        if (!TryParseStatusLine(statusLine, out var statusCode, out var reasonPhrase))
        {
            return HttpDecodeResult.Fail(HttpDecodeError.InvalidStatusLine);
        }

        // 3. Parse headers using span-based parsing
        var headersData = headerSection[(statusLineEnd + 2)..];
        var headers = ParseHeaders(headersData);

        // 4. Build response object
        response = new HttpResponseMessage
        {
            StatusCode = (HttpStatusCode)statusCode,
            ReasonPhrase = reasonPhrase,
            Version = new Version(1, 1)
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
                if (!IsContentHeader(name)) continue;
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
        var (bodyResult, bodyBytes, bodyConsumed, trailerHeaders) = ParseBody(bodyData, headers);
        if (!bodyResult.Success)
        {
            return bodyResult;
        }

        if (bodyBytes == null)
        {
            return HttpDecodeResult.Incomplete();
        }

        // 7. Create content
        var content = new ByteArrayContent(bodyBytes);

        foreach (var (name, values) in headers)
        {
            if (!IsContentHeader(name)) continue;
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

    // ── Status Line Parsing ─────────────────────────────────────────────────────
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
            reasonPhrase = Encoding.ASCII.GetString(line[reasonStart..]);
        }

        return statusCode is >= 100 and < 600;
    }

    // ── Header Parsing ──────────────────────────────────────────────────────────

    private Dictionary<string, List<string>> ParseHeaders(ReadOnlySpan<byte> data)
    {
        var headers = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var pos = 0;
        var fieldCount = 0;

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
                throw new HttpDecoderException(HttpDecodeError.TooManyHeaders,
                    $"Received {fieldCount} fields; limit is {_maxHeaderCount}.");
            }

            var line = data[pos..lineEnd];
            var colonIdx = line.IndexOf((byte)':');

            // RFC 9112 §5.1 / RFC 7230 §3.2: every header field MUST contain a colon.
            // colonIdx == -1: no colon present; colonIdx == 0: empty field name — both are invalid.
            if (colonIdx <= 0)
            {
                throw new HttpDecoderException(HttpDecodeError.InvalidHeader);
            }

            var name = WellKnownHeaders.TrimOws(line[..colonIdx]);
            var value = WellKnownHeaders.TrimOws(line[(colonIdx + 1)..]);

            var nameStr = Encoding.ASCII.GetString(name);
            var valueStr = Encoding.ASCII.GetString(value);

            // RFC 9112 §5.5: Header field values MUST NOT contain CR, LF, or NUL characters.
            if (valueStr.IndexOfAny(['\r', '\n', '\0']) >= 0)
            {
                throw new HttpDecoderException(
                    HttpDecodeError.InvalidFieldValue,
                    $"Header '{nameStr}' contains a CR, LF, or NUL character in its value.");
            }

            if (!headers.TryGetValue(nameStr, out var values))
            {
                headers[nameStr] = values = [];
            }

            values.Add(valueStr);

            pos = lineEnd + 2;
        }

        return headers;
    }

    // ── Body Parsing ────────────────────────────────────────────────────────────
    private (HttpDecodeResult result, byte[]? body, int consumed, Dictionary<string, List<string>>? trailers)
        ParseBody(ReadOnlySpan<byte> data, Dictionary<string, List<string>> headers)
    {
        var transferEncoding = GetSingleHeader(headers, "Transfer-Encoding");
        var contentLength = GetContentLengthHeader(headers);

        // RFC 9112 Section 6.3: Transfer-Encoding takes precedence
        if (!string.IsNullOrEmpty(transferEncoding) &&
            transferEncoding.Contains("chunked", StringComparison.OrdinalIgnoreCase))
        {
            // RFC 9112 §6.3 / Security: Reject responses with both Transfer-Encoding and Content-Length
            // to prevent HTTP request smuggling attacks.
            if (contentLength.HasValue)
            {
                return (HttpDecodeResult.Fail(HttpDecodeError.ChunkedWithContentLength), null, 0, null);
            }

            return ParseChunkedBody(data);
        }

        if (!contentLength.HasValue) return (HttpDecodeResult.Ok(), [], 0, null);
        var len = contentLength.Value;

        if (len > _maxBodySize)
        {
            return (HttpDecodeResult.Fail(HttpDecodeError.InvalidContentLength), null, 0, null);
        }

        if (data.Length < len)
        {
            return (HttpDecodeResult.Incomplete(), null, 0, null);
        }

        var body = data[..len].ToArray();
        return (HttpDecodeResult.Ok(), body, len, null);

        // No Content-Length and no Transfer-Encoding: empty body
    }

    private (HttpDecodeResult result, byte[]? body, int consumed, Dictionary<string, List<string>>? trailers)
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
                return (HttpDecodeResult.Incomplete(), null, 0, null);
            }

            // Parse chunk size (hex) and optional chunk extensions (RFC 9112 §7.1.1)
            var sizeLine = data[pos..lineEnd];
            var semiIdx = sizeLine.IndexOf((byte)';');
            var sizeSpan = semiIdx >= 0 ? sizeLine[..semiIdx] : sizeLine;
            var extSpan = semiIdx >= 0 ? sizeLine[(semiIdx + 1)..] : ReadOnlySpan<byte>.Empty;

            if (!TryParseChunkExtensions(extSpan))
            {
                return (HttpDecodeResult.Fail(HttpDecodeError.InvalidChunkExtension), null, 0, null);
            }

            if (!TryParseHex(sizeSpan, out var chunkSize))
            {
                return (HttpDecodeResult.Fail(HttpDecodeError.InvalidChunkSize), null, 0, null);
            }

            pos = lineEnd + 2;

            // Last chunk (size = 0)
            if (chunkSize == 0)
            {
                var remaining = data[pos..];

                // Empty trailer section: just a CRLF terminator
                if (remaining.Length >= 2 && remaining[0] == '\r' && remaining[1] == '\n')
                {
                    var result = _bodyLength > 0
                        ? _bodyBuffer.AsSpan(0, _bodyLength).ToArray()
                        : [];
                    return (HttpDecodeResult.Ok(), result, pos + 2, null);
                }

                // Trailer headers present: look for the CRLFCRLF terminator
                var trailerEnd = FindCrlfCrlf(remaining);
                if (trailerEnd >= 0)
                {
                    var trailerData = remaining[..(trailerEnd + 2)]; // include final header CRLF
                    var trailers = ParseHeaders(trailerData);

                    var result = _bodyLength > 0
                        ? _bodyBuffer.AsSpan(0, _bodyLength).ToArray()
                        : [];
                    return (HttpDecodeResult.Ok(), result, pos + trailerEnd + 4, trailers);
                }

                return (HttpDecodeResult.Incomplete(), null, 0, null);
            }

            // Validate chunk size
            if (chunkSize > _maxBodySize || _bodyLength + chunkSize > _maxBodySize)
            {
                return (HttpDecodeResult.Fail(HttpDecodeError.InvalidContentLength), null, 0, null);
            }

            // Need chunk data + CRLF
            if (pos + chunkSize + 2 > data.Length)
            {
                return (HttpDecodeResult.Incomplete(), null, 0, null);
            }

            // Append chunk data to body buffer
            EnsureBodyCapacity(_bodyLength + chunkSize);
            data.Slice(pos, chunkSize).CopyTo(_bodyBuffer.AsSpan(_bodyLength));
            _bodyLength += chunkSize;

            pos += chunkSize + 2; // Skip chunk data and trailing CRLF
        }

        return (HttpDecodeResult.Incomplete(), null, 0, null);
    }

    // ── Utilities ───────────────────────────────────────────────────────────────

    private static bool IsNoBodyResponse(int statusCode) =>
        statusCode is >= 100 and < 200 or 204 or 304;

    private static bool IsContentHeader(string name) =>
        name.StartsWith("content-", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("content-length", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("content-type", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("allow", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("expires", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("last-modified", StringComparison.OrdinalIgnoreCase);

    private static int? GetContentLengthHeader(Dictionary<string, List<string>> headers)
    {
        if (!headers.TryGetValue("Content-Length", out var values) || values.Count == 0)
            return null;

        // RFC 9112 Section 6.3: Multiple Content-Length with different values is error
        if (values.Count > 1)
        {
            var first = values[0];
            for (var i = 1; i < values.Count; i++)
            {
                if (!values[i].Equals(first, StringComparison.Ordinal))
                {
                    throw new HttpDecoderException(
                        HttpDecodeError.MultipleContentLengthValues,
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

    // ── Span Search Utilities ───────────────────────────────────────────────────

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

    // ── Chunk Extension Parsing ─────────────────────────────────────────────────

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

    // ── Number Parsing ──────────────────────────────────────────────────────────

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