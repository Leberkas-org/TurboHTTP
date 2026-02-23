using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;

namespace TurboHttp.Protocol;

public sealed class Http10Decoder
{
    private ReadOnlyMemory<byte> _remainder = ReadOnlyMemory<byte>.Empty;

    public bool TryDecode(ReadOnlyMemory<byte> incomingData, out HttpResponseMessage? response)
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
        if (lines.Length == 0) return false;

        ValidateStatusLine(lines[0]);
        var headers = ParseHeaders(lines[1..]);
        var bodyStart = headerEnd + GetHeaderDelimiterLength(working.Span, headerEnd);
        var bodyData = working[bodyStart..];

        var statusCode = ParseStatusCode(lines[0]);

        // No-body responses: 204 and 304 always have empty body (RFC 1945 §7)
        if (statusCode is 204 or 304)
        {
            response = BuildResponse(lines[0], headers, []);
            return true;
        }

        var contentLength = GetContentLength(headers);
        if (contentLength.HasValue)
        {
            if (bodyData.Length < contentLength.Value)
            {
                _remainder = working;
                return false;
            }

            response = BuildResponse(lines[0], headers, bodyData.Span[..contentLength.Value].ToArray());
            return true;
        }

        response = BuildResponse(lines[0], headers, bodyData.ToArray());
        return true;
    }

    public bool TryDecodeEof(out HttpResponseMessage? response)
    {
        response = null;
        if (_remainder.IsEmpty) return false;

        var span = _remainder.Span;
        var headerEnd = FindHeaderEnd(span);
        if (headerEnd < 0) return false;

        var headerBytes = _remainder[..headerEnd].ToArray();
        var lines = SplitHeaderLines(headerBytes);
        if (lines.Length == 0) return false;

        ValidateStatusLine(lines[0]);
        var headers = ParseHeaders(lines[1..]);
        var index = headerEnd + GetHeaderDelimiterLength(span, headerEnd);
        var body = _remainder[index..].ToArray();

        response = BuildResponse(lines[0], headers, body);
        _remainder = ReadOnlyMemory<byte>.Empty;
        return true;
    }

    public void Reset() => _remainder = ReadOnlyMemory<byte>.Empty;

    private static void ValidateStatusLine(string statusLine)
    {
        var parts = statusLine.Split(' ', 3);
        if (parts.Length < 2 || !int.TryParse(parts[1], out var code))
        {
            throw new HttpDecoderException(
                HttpDecodeError.InvalidStatusLine,
                $"Line: '{statusLine}'.");
        }

        if (code is < 100 or > 999)
        {
            throw new HttpDecoderException(
                HttpDecodeError.InvalidStatusLine,
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
        if (!headers.TryGetValue("Content-Length", out var clValues) || clValues.Count == 0)
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
                    throw new HttpDecoderException(
                        HttpDecodeError.MultipleContentLengthValues,
                        $"Values '{first}' and '{clValues[i]}' conflict.");
                }
            }
        }

        if (!int.TryParse(clValues[0], out var len))
        {
            throw new HttpDecoderException(
                HttpDecodeError.InvalidContentLength,
                $"Value: '{clValues[0]}'.");
        }

        if (len < 0)
        {
            throw new HttpDecoderException(
                HttpDecodeError.InvalidContentLength,
                $"Value {len} is negative.");
        }

        return len;
    }

    private static Dictionary<string, List<string>> ParseHeaders(string[] lines)
    {
        var headers = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        string? lastHeader = null;

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
                lastValues[^1] = lastValue + " " + rawLine.Trim();
                continue;
            }

            var colon = rawLine.IndexOf(':');
            if (colon <= 0)
            {
                throw new HttpDecoderException(HttpDecodeError.InvalidHeader);
            }

            var name = rawLine[..colon];

            // Validate header name: no spaces allowed
            if (name.Contains(' '))
            {
                throw new HttpDecoderException(HttpDecodeError.InvalidFieldName);
            }

            name = name.Trim();
            var value = rawLine[(colon + 1)..].Trim();

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
        "Content-Type", "Content-Length", "Content-Encoding", "Content-Language", "Content-Location", "Content-MD5",
        "Content-Range", "Content-Disposition", "Expires", "Last-Modified"
    };

    private static HttpResponseMessage BuildResponse(string statusLine, Dictionary<string, List<string>> headers,
        byte[] body)
    {
        var parts = statusLine.Split(' ', 3);
        var statusCode = 500;
        if (parts.Length >= 2 && int.TryParse(parts[1], out var code)) statusCode = code;

        var reasonPhrase = parts.Length > 2 ? parts[2] : string.Empty;
        var response = new HttpResponseMessage((HttpStatusCode)statusCode)
        {
            ReasonPhrase = reasonPhrase,
            Version = new Version(1, 0)
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
        if (a.IsEmpty) return b;
        if (b.IsEmpty) return a;
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
        var headerText = Encoding.GetEncoding("ISO-8859-1").GetString(headerBytes);
        return headerText.Split(["\r\n", "\n"], StringSplitOptions.None);
    }
}