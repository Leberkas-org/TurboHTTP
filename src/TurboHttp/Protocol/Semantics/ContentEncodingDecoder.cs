using System.Buffers;
using System.IO.Compression;

namespace TurboHttp.Protocol.Semantics;

/// <summary>
/// RFC 9110 §8.4 — Content-Encoding decompression for HTTP responses.
/// Handles gzip, deflate, br (Brotli), and identity encodings.
/// For stacked encodings (e.g. "gzip, br"), decodes in reverse order
/// (outermost encoding decoded first).
/// </summary>
internal static class ContentEncodingDecoder
{
    /// <summary>
    /// Returns <see langword="true"/> when the given encoding token is one this decoder can handle
    /// (gzip, x-gzip, deflate, br, identity, or empty/null).
    /// </summary>
    public static bool IsSupported(string? encoding)
    {
        if (string.IsNullOrWhiteSpace(encoding))
        {
            return true;
        }

        // A comma-separated list may contain multiple tokens; all must be supported.
        var tokens = encoding.Split(',');

        for (var i = 0; i < tokens.Length; i++)
        {
            var token = tokens[i].Trim();

            if (string.IsNullOrEmpty(token) ||
                token.Equals(WellKnownHeaders.Identity, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!IsSupportedToken(token))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Decompresses <paramref name="body"/> according to the Content-Encoding token list.
    /// Returns the decompressed bytes in an <see cref="ArrayPool{T}"/>-rented buffer.
    /// Caller MUST return <c>Buffer</c> to <see cref="ArrayPool{T}.Shared"/> after use,
    /// unless <c>Length</c> is 0 (empty body returns <see cref="Array.Empty{T}()"/> which is safe to return).
    /// </summary>
    public static (byte[] Buffer, int Length) Decompress(ReadOnlySpan<byte> body, string? contentEncoding)
    {
        if (string.IsNullOrWhiteSpace(contentEncoding))
        {
            return body.IsEmpty ? (Array.Empty<byte>(), 0) : CopyToPool(body);
        }

        // RFC 9110 §8.4: Content-Encoding is a comma-separated list.
        // Decodings are applied in reverse order (last encoding is outermost).
        var encodings = contentEncoding.Split(',');

        byte[]? curBuffer = null;
        var curLength = 0;
        var hasCurrent = false;

        for (var i = encodings.Length - 1; i >= 0; i--)
        {
            var encoding = encodings[i].Trim();

            if (string.IsNullOrEmpty(encoding) ||
                encoding.Equals(WellKnownHeaders.Identity, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var input = hasCurrent
                ? curBuffer!.AsSpan(0, curLength)
                : body;

            var (newBuf, newLen) = DecompressSingle(input, encoding);

            if (hasCurrent)
            {
                ArrayPool<byte>.Shared.Return(curBuffer!);
            }

            curBuffer = newBuf;
            curLength = newLen;
            hasCurrent = true;
        }

        if (!hasCurrent)
        {
            return body.IsEmpty ? (Array.Empty<byte>(), 0) : CopyToPool(body);
        }

        return (curBuffer!, curLength);
    }

    private static bool IsSupportedToken(string token)
    {
        return token.Equals(WellKnownHeaders.Gzip, StringComparison.OrdinalIgnoreCase) ||
               token.Equals(WellKnownHeaders.XGzip, StringComparison.OrdinalIgnoreCase) ||
               token.Equals(WellKnownHeaders.Deflate, StringComparison.OrdinalIgnoreCase) ||
               token.Equals(WellKnownHeaders.Brotli, StringComparison.OrdinalIgnoreCase);
    }

    private static (byte[] Buffer, int Length) DecompressSingle(ReadOnlySpan<byte> data, string encoding)
    {
        if (data.IsEmpty)
        {
            return ([], 0);
        }

        try
        {
            if (encoding.Equals(WellKnownHeaders.Gzip, StringComparison.OrdinalIgnoreCase) ||
                encoding.Equals(WellKnownHeaders.XGzip, StringComparison.OrdinalIgnoreCase))
            {
                return DecompressGzip(data);
            }

            if (encoding.Equals(WellKnownHeaders.Deflate, StringComparison.OrdinalIgnoreCase))
            {
                return DecompressDeflate(data);
            }

            if (encoding.Equals(WellKnownHeaders.Brotli, StringComparison.OrdinalIgnoreCase))
            {
                return DecompressBrotli(data);
            }

            // Unknown encoding: RFC 9110 §8.4 — client cannot process unknown response encoding.
            throw new HttpDecoderException(HttpDecoderError.DecompressionFailed,
                $"RFC 9110 §8.4: Unknown Content-Encoding '{encoding}'; cannot decompress response.");
        }
        catch (HttpDecoderException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new HttpDecoderException(HttpDecoderError.DecompressionFailed,
                $"RFC 9110 §8.4: Decompression failed for encoding '{encoding}': {ex.Message}");
        }
    }

    private static (byte[] Buffer, int Length) DecompressGzip(ReadOnlySpan<byte> data)
    {
        using var input = SpanToStream(data);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        return ReadToPool(gzip, data.Length * 2);
    }

    private static (byte[] Buffer, int Length) DecompressDeflate(ReadOnlySpan<byte> data)
    {
        // RFC 9110 §8.4.2: "deflate" is the zlib format (RFC 1950), not raw DEFLATE.
        // However, some servers send raw DEFLATE (RFC 1951) without the zlib wrapper.
        // Try zlib first; fall back to raw DEFLATE if it fails.
        try
        {
            using var input = SpanToStream(data);
            using var deflate = new ZLibStream(input, CompressionMode.Decompress);
            return ReadToPool(deflate, data.Length * 2);
        }
        catch
        {
            using var input = SpanToStream(data);
            using var deflate = new DeflateStream(input, CompressionMode.Decompress);
            return ReadToPool(deflate, data.Length * 2);
        }
    }

    private static (byte[] Buffer, int Length) DecompressBrotli(ReadOnlySpan<byte> data)
    {
        using var input = SpanToStream(data);
        using var brotli = new BrotliStream(input, CompressionMode.Decompress);
        return ReadToPool(brotli, data.Length * 3);
    }

    private static MemoryStream SpanToStream(ReadOnlySpan<byte> data)
    {
        var ms = new MemoryStream(data.Length);
        ms.Write(data);
        ms.Position = 0;
        return ms;
    }

    private static (byte[] Buffer, int Length) ReadToPool(Stream source, int estimatedSize)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(Math.Max(estimatedSize, 256));
        var written = 0;

        while (true)
        {
            if (written == buffer.Length)
            {
                var larger = ArrayPool<byte>.Shared.Rent(buffer.Length * 2);
                buffer.AsSpan(0, written).CopyTo(larger);
                ArrayPool<byte>.Shared.Return(buffer);
                buffer = larger;
            }

            var read = source.Read(buffer, written, buffer.Length - written);
            if (read == 0)
            {
                break;
            }

            written += read;
        }

        return (buffer, written);
    }

    private static (byte[] Buffer, int Length) CopyToPool(ReadOnlySpan<byte> data)
    {
        var buf = ArrayPool<byte>.Shared.Rent(data.Length);
        data.CopyTo(buf);
        return (buf, data.Length);
    }
}
