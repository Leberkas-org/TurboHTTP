using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;

namespace TurboHttp.Protocol;

public static class Http10Encoder
{
    public static int Encode(HttpRequestMessage request, ref Memory<byte> buffer, bool absoluteForm = false)
    {
        ValidateMethod(request.Method.Method);

        var bodyBytes = ReadBody(request.Content);

        var headers = MergeHeaders(request);
        EnforceHttp10Headers(request, headers, bodyBytes.Length);

        var span = buffer.Span;
        var bytesWritten = 0;

        bytesWritten += WriteAscii(span[bytesWritten..], request.Method.Method);
        bytesWritten += WriteAscii(span[bytesWritten..], " ");
        bytesWritten += WriteAscii(span[bytesWritten..], EncodeRequestUri(request.RequestUri!, absoluteForm));
        bytesWritten += WriteAscii(span[bytesWritten..], " HTTP/1.0\r\n");

        foreach (var (name, values) in headers)
        {
            foreach (var value in values)
            {
                ValidateHeaderValue(name, value);
                bytesWritten += WriteAscii(span[bytesWritten..], name);
                bytesWritten += WriteAscii(span[bytesWritten..], ": ");
                bytesWritten += WriteAscii(span[bytesWritten..], value);
                bytesWritten += WriteAscii(span[bytesWritten..], "\r\n");
            }
        }

        bytesWritten += WriteAscii(span[bytesWritten..], "\r\n");

        if (bodyBytes.Length <= 0)
        {
            return bytesWritten;
        }

        if (bytesWritten + bodyBytes.Length > buffer.Length)
        {
            throw new InvalidOperationException();
        }

        bodyBytes.Span.CopyTo(span[bytesWritten..]);
        bytesWritten += bodyBytes.Length;

        return bytesWritten;
    }


    private static ReadOnlyMemory<byte> ReadBody(HttpContent? content)
    {
        if (content == null)
        {
            return ReadOnlyMemory<byte>.Empty;
        }

        var bytes = content.ReadAsByteArrayAsync().Result;
        return bytes.AsMemory();
    }

    private static int WriteAscii(Span<byte> destination, string value)
    {
        var needed = Encoding.ASCII.GetByteCount(value);
        if (needed > destination.Length)
        {
            throw new InvalidOperationException();
        }

        return Encoding.ASCII.GetBytes(value.AsSpan(), destination);
    }

    private static string EncodeRequestUri(Uri uri, bool absoluteForm = false)
    {
        if (absoluteForm)
        {
            return uri.GetLeftPart(UriPartial.Query);
        }

        var pathAndQuery = uri.GetComponents(
            UriComponents.PathAndQuery,
            UriFormat.UriEscaped);

        return string.IsNullOrEmpty(pathAndQuery) ? "/" : pathAndQuery;
    }

    private static Dictionary<string, List<string>> MergeHeaders(HttpRequestMessage request)
    {
        var headers = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var header in request.Headers)
        {
            headers[header.Key] = [..header.Value];
        }

        if (request.Content?.Headers is null)
        {
            return headers;
        }

        foreach (var header in request.Content.Headers)
        {
            if (!headers.TryGetValue(header.Key, out var list))
            {
                list = [];
                headers[header.Key] = list;
            }

            list.AddRange(header.Value);
        }

        return headers;
    }

    private static void EnforceHttp10Headers(HttpRequestMessage request, Dictionary<string, List<string>> headers,
        int bodyLength)
    {
        headers.Remove("Host");

        headers.Remove("Connection");
        headers.Remove("Keep-Alive");

        headers.Remove("Transfer-Encoding");

        if (bodyLength > 0)
        {
            headers["Content-Length"] = [bodyLength.ToString()];
        }
        else
        {
            headers.Remove("Content-Length");
            headers.Remove("Content-Type");

            // For methods that carry a body (POST, PUT, PATCH), emit Content-Length: 0
            // so the server knows the body is explicitly empty rather than having to
            // read until connection-close (HTTP/1.0 §7.2).
            var method = request.Method.Method;
            if (method is "POST" or "PUT" or "PATCH")
            {
                headers["Content-Length"] = ["0"];
            }
        }
    }

    private static void ValidateMethod(string method)
    {
        if (method.Any(char.IsLower))
        {
            throw new ArgumentException($"HTTP/1.0 method must be uppercase: {method}", nameof(method));
        }
    }

    private static void ValidateHeaderValue(string name, string value)
    {
        if (value.AsSpan().ContainsAny('\r', '\n', '\0'))
        {
            throw new ArgumentException(name);
        }
    }
}