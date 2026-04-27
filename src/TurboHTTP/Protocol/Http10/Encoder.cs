using System.Text;
using TurboHTTP.Internal;
using TurboHTTP.Protocol.Semantics;

namespace TurboHTTP.Protocol.Http10;

internal static class Encoder
{
    public static int Encode(HttpRequestMessage request, ref Span<byte> buffer, bool absoluteForm = false)
    {
        ValidateMethod(request.Method.Method);

        // Read body directly into a span-friendly form — single copy instead of triple-copy.
        var bodyLength = ReadBodyLength(request.Content);

        var span = buffer;
        var bytesWritten = 0;

        // Request line
        bytesWritten += WriteAscii(span[bytesWritten..], request.Method.Method);
        bytesWritten += WriteAscii(span[bytesWritten..], " ");
        bytesWritten += WriteAscii(span[bytesWritten..], EncodeRequestUri(request.RequestUri!, absoluteForm));
        bytesWritten += WriteAscii(span[bytesWritten..], " HTTP/1.0\r\n");

        // Write headers directly to span — avoids Dictionary + List<string> allocations.
        // HTTP/1.0 enforcement: remove Host, ensure Connection: Keep-Alive, fix Content-Length.
        var wroteConnection = false;
        var method = request.Method.Method;

        foreach (var header in request.Headers)
        {
            // HTTP/1.0: skip Host header (not defined in RFC 1945)
            if (string.Equals(header.Key, "Host", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // HTTP/1.0: skip Transfer-Encoding (not supported)
            if (string.Equals(header.Key, "Transfer-Encoding", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // Track if user provided Connection header
            if (string.Equals(header.Key, "Connection", StringComparison.OrdinalIgnoreCase))
            {
                wroteConnection = true;
            }

            // Skip Content-Length/Content-Type from request headers — we control these below
            if (string.Equals(header.Key, "Content-Length", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(header.Key, "Content-Type", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (var value in header.Value)
            {
                ValidateHeaderValue(header.Key, value);
                bytesWritten += WriteAscii(span[bytesWritten..], header.Key);
                bytesWritten += WriteAscii(span[bytesWritten..], ": ");
                bytesWritten += WriteAscii(span[bytesWritten..], value);
                bytesWritten += WriteAscii(span[bytesWritten..], "\r\n");
            }
        }

        // Write content headers (except Content-Length which we control)
        if (request.Content?.Headers is not null)
        {
            foreach (var header in request.Content.Headers)
            {
                if (string.Equals(header.Key, "Content-Length", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                foreach (var value in header.Value)
                {
                    ValidateHeaderValue(header.Key, value);
                    bytesWritten += WriteAscii(span[bytesWritten..], header.Key);
                    bytesWritten += WriteAscii(span[bytesWritten..], ": ");
                    bytesWritten += WriteAscii(span[bytesWritten..], value);
                    bytesWritten += WriteAscii(span[bytesWritten..], "\r\n");
                }
            }
        }

        // HTTP/1.0 enforced headers
        if (!wroteConnection)
        {
            bytesWritten += WriteAscii(span[bytesWritten..], "Connection: Keep-Alive\r\n");
        }

        if (bodyLength > 0)
        {
            bytesWritten += WriteAscii(span[bytesWritten..], "Content-Length: ");
            bytesWritten += WriteAscii(span[bytesWritten..], bodyLength.ToString());
            bytesWritten += WriteAscii(span[bytesWritten..], "\r\n");
        }
        else if (method is "POST" or "PUT" or "PATCH")
        {
            bytesWritten += WriteAscii(span[bytesWritten..], "Content-Length: 0\r\n");
        }

        bytesWritten += WriteAscii(span[bytesWritten..], "\r\n");

        // Write body — single copy directly into the target span.
        if (bodyLength > 0)
        {
            if (bytesWritten + bodyLength > buffer.Length)
            {
                throw new InvalidOperationException();
            }

            using var stream = request.Content!.ReadAsStream();
            var bodySpan = span[bytesWritten..];
            var totalRead = 0;
            while (totalRead < bodyLength)
            {
                var read = stream.Read(bodySpan[totalRead..]);
                if (read == 0)
                {
                    break;
                }

                totalRead += read;
            }

            bytesWritten += totalRead;
        }

        return bytesWritten;
    }

    private static int ReadBodyLength(HttpContent? content)
    {
        if (content == null)
        {
            return 0;
        }

        if (content.Headers.ContentLength is { } cl)
        {
            return (int)cl;
        }

        // HTTP/1.0 has no chunked transfer encoding, so we must buffer to
        // determine the length. ReadAsStream triggers SerializeToStream once
        // and caches internally. Do NOT dispose — the encoder reads it again.
        var stream = content.ReadAsStream();
        if (stream.CanSeek)
        {
            return (int)stream.Length;
        }

        using var ms = RecyclableStreams.Manager.GetStream();
        stream.CopyTo(ms);
        content.Headers.ContentLength = ms.Length;
        return (int)ms.Length;
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
            return UriSanitizer.FormatAbsoluteWithoutUserInfo(uri);
        }

        var pathAndQuery = uri.GetComponents(
            UriComponents.PathAndQuery,
            UriFormat.UriEscaped);

        return string.IsNullOrEmpty(pathAndQuery) ? "/" : pathAndQuery;
    }


    private static void ValidateMethod(string method)
    {
        foreach (var c in method)
        {
            if (char.IsLower(c))
            {
                throw new ArgumentException($"HTTP/1.0 method must be uppercase: {method}", nameof(method));
            }
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