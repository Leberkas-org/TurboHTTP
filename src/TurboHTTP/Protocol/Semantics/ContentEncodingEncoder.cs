using System.IO.Compression;
using TurboHTTP.Internal;

namespace TurboHTTP.Protocol.Semantics;

/// <summary>
/// RFC 9110 §8.4 — Content-Encoding compression for HTTP request bodies.
/// Handles gzip, deflate, and br (Brotli) encodings.
/// </summary>
internal static class ContentEncodingEncoder
{
    /// <summary>
    /// Compresses <paramref name="body"/> using the specified encoding.
    /// Returns a readable <see cref="Stream"/> positioned at 0 containing the compressed bytes.
    /// Caller MUST dispose the returned stream after use.
    /// Returns <see cref="Stream.Null"/> for empty input.
    /// Throws <see cref="ArgumentException"/> for unknown encodings.
    /// </summary>
    /// <param name="body">The uncompressed request body bytes.</param>
    /// <param name="encoding">The encoding to apply (e.g. "gzip", "deflate", "br").</param>
    /// <returns>A readable stream containing the compressed bytes.</returns>
    public static Stream Compress(ReadOnlySpan<byte> body, string encoding)
    {
        if (body.IsEmpty)
        {
            return Stream.Null;
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

    private static Stream CompressGzip(ReadOnlySpan<byte> data)
    {
        var output = RecyclableStreams.Manager.GetStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            gzip.Write(data);
        }

        output.Position = 0;
        return output;
    }

    private static Stream CompressDeflate(ReadOnlySpan<byte> data)
    {
        var output = RecyclableStreams.Manager.GetStream();
        using (var deflate = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            deflate.Write(data);
        }

        output.Position = 0;
        return output;
    }

    private static Stream CompressBrotli(ReadOnlySpan<byte> data)
    {
        var output = RecyclableStreams.Manager.GetStream();
        using (var brotli = new BrotliStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            brotli.Write(data);
        }

        output.Position = 0;
        return output;
    }
}
