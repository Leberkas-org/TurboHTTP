using System;
using System.IO;
using System.IO.Compression;

namespace TurboHttp.Protocol.RFC9110;

/// <summary>
/// RFC 9110 §8.4 — Content-Encoding compression for HTTP request bodies.
/// Handles gzip, deflate, and br (Brotli) encodings.
/// </summary>
internal static class ContentEncodingEncoder
{
    /// <summary>
    /// Compresses <paramref name="body"/> using the specified encoding.
    /// Throws <see cref="ArgumentException"/> for unknown encodings.
    /// </summary>
    /// <param name="body">The uncompressed request body bytes.</param>
    /// <param name="encoding">The encoding to apply (e.g. "gzip", "deflate", "br").</param>
    /// <returns>Compressed body bytes.</returns>
    public static byte[] Compress(byte[] body, string encoding)
    {
        if (body.Length == 0)
        {
            return body;
        }

        if (encoding.Equals(WellKnownHeaders.Gzip, StringComparison.OrdinalIgnoreCase) ||
            encoding.Equals(WellKnownHeaders.XGzip, StringComparison.OrdinalIgnoreCase))
        {
            return CompressGzip(body);
        }

        if (encoding.Equals(WellKnownHeaders.Deflate, StringComparison.OrdinalIgnoreCase))
        {
            return CompressDeflate(body);
        }

        if (encoding.Equals(WellKnownHeaders.Brotli, StringComparison.OrdinalIgnoreCase))
        {
            return CompressBrotli(body);
        }

        throw new ArgumentException(
            $"RFC 9110 §8.4: Unknown Content-Encoding '{encoding}'; cannot compress request body.",
            nameof(encoding));
    }

    private static byte[] CompressGzip(byte[] data)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            gzip.Write(data, 0, data.Length);
        }

        return output.ToArray();
    }

    private static byte[] CompressDeflate(byte[] data)
    {
        using var output = new MemoryStream();
        using (var deflate = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            deflate.Write(data, 0, data.Length);
        }

        return output.ToArray();
    }

    private static byte[] CompressBrotli(byte[] data)
    {
        using var output = new MemoryStream();
        using (var brotli = new BrotliStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            brotli.Write(data, 0, data.Length);
        }

        return output.ToArray();
    }
}
