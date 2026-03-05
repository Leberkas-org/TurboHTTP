using System;
using System.IO;
using System.IO.Compression;

namespace TurboHttp.Protocol;

/// <summary>
/// RFC 9110 §8.4 — Content-Encoding decompression for HTTP responses.
/// Handles gzip, deflate, br (Brotli), and identity encodings.
/// For stacked encodings (e.g. "gzip, br"), decodes in reverse order
/// (outermost encoding decoded first).
/// </summary>
internal static class ContentEncodingDecoder
{
    /// <summary>
    /// Decompresses <paramref name="body"/> according to the Content-Encoding token list.
    /// Returns the original bytes unchanged if encoding is null, empty, or "identity".
    /// Throws <see cref="HttpDecoderException"/> with <see cref="HttpDecodeError.DecompressionFailed"/>
    /// on unknown encodings or decompression failures.
    /// </summary>
    /// <param name="body">The compressed response body bytes.</param>
    /// <param name="contentEncoding">The Content-Encoding header value (may be null or empty).</param>
    /// <returns>Decompressed body bytes, or the original bytes for identity/no encoding.</returns>
    public static byte[] Decompress(byte[] body, string? contentEncoding)
    {
        if (string.IsNullOrWhiteSpace(contentEncoding))
        {
            return body;
        }

        // RFC 9110 §8.4.1: Content-Encoding is a comma-separated list.
        // Decodings are applied in reverse order (last encoding is outermost).
        var encodings = contentEncoding.Split(',');

        var current = body;

        // Decode from last to first (reverse order)
        for (var i = encodings.Length - 1; i >= 0; i--)
        {
            var encoding = encodings[i].Trim();

            if (string.IsNullOrEmpty(encoding) || encoding.Equals("identity", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            current = DecompressSingle(current, encoding);
        }

        return current;
    }

    private static byte[] DecompressSingle(byte[] data, string encoding)
    {
        if (data.Length == 0)
        {
            return data;
        }

        try
        {
            if (encoding.Equals("gzip", StringComparison.OrdinalIgnoreCase) ||
                encoding.Equals("x-gzip", StringComparison.OrdinalIgnoreCase))
            {
                return DecompressGzip(data);
            }

            if (encoding.Equals("deflate", StringComparison.OrdinalIgnoreCase))
            {
                return DecompressDeflate(data);
            }

            if (encoding.Equals("br", StringComparison.OrdinalIgnoreCase))
            {
                return DecompressBrotli(data);
            }

            // Unknown encoding: RFC 9110 §8.4 — client cannot process unknown response encoding.
            throw new HttpDecoderException(HttpDecodeError.DecompressionFailed,
                $"RFC 9110 §8.4: Unknown Content-Encoding '{encoding}'; cannot decompress response.");
        }
        catch (HttpDecoderException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new HttpDecoderException(HttpDecodeError.DecompressionFailed,
                $"RFC 9110 §8.4: Decompression failed for encoding '{encoding}': {ex.Message}");
        }
    }

    private static byte[] DecompressGzip(byte[] data)
    {
        using var input = new MemoryStream(data, writable: false);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        gzip.CopyTo(output);
        return output.ToArray();
    }

    private static byte[] DecompressDeflate(byte[] data)
    {
        // RFC 9110 §8.4.1.2: "deflate" is the zlib format (RFC 1950), not raw DEFLATE.
        // However, some servers send raw DEFLATE (RFC 1951) without the zlib wrapper.
        // Try zlib first; fall back to raw DEFLATE if it fails.
        try
        {
            using var input = new MemoryStream(data, writable: false);
            using var deflate = new ZLibStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            deflate.CopyTo(output);
            return output.ToArray();
        }
        catch
        {
            // Fall back to raw DEFLATE
            using var input = new MemoryStream(data, writable: false);
            using var deflate = new DeflateStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            deflate.CopyTo(output);
            return output.ToArray();
        }
    }

    private static byte[] DecompressBrotli(byte[] data)
    {
        using var input = new MemoryStream(data, writable: false);
        using var brotli = new BrotliStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        brotli.CopyTo(output);
        return output.ToArray();
    }
}