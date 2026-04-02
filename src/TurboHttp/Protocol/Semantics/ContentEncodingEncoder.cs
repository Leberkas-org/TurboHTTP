using System.Buffers;
using System.IO.Compression;

namespace TurboHttp.Protocol.Semantics;

/// <summary>
/// RFC 9110 §8.4 — Content-Encoding compression for HTTP request bodies.
/// Handles gzip, deflate, and br (Brotli) encodings.
/// </summary>
internal static class ContentEncodingEncoder
{
    /// <summary>
    /// Compresses <paramref name="body"/> using the specified encoding.
    /// Returns the compressed bytes in an <see cref="ArrayPool{T}"/>-rented buffer.
    /// Caller MUST return <c>Buffer</c> to <see cref="ArrayPool{T}.Shared"/> after use,
    /// unless <c>Length</c> is 0 (<see cref="Array.Empty{T}"/> is returned and is safe to skip).
    /// Throws <see cref="ArgumentException"/> for unknown encodings.
    /// </summary>
    /// <param name="body">The uncompressed request body bytes.</param>
    /// <param name="encoding">The encoding to apply (e.g. "gzip", "deflate", "br").</param>
    /// <returns>Pool-rented buffer and the number of valid bytes written.</returns>
    public static (byte[] Buffer, int Length) Compress(ReadOnlySpan<byte> body, string encoding)
    {
        if (body.IsEmpty)
        {
            return ([], 0);
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

    private static (byte[] Buffer, int Length) CompressGzip(ReadOnlySpan<byte> data)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            gzip.Write(data);
        }

        return CopyToPool(output);
    }

    private static (byte[] Buffer, int Length) CompressDeflate(ReadOnlySpan<byte> data)
    {
        using var output = new MemoryStream();
        using (var deflate = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            deflate.Write(data);
        }

        return CopyToPool(output);
    }

    private static (byte[] Buffer, int Length) CompressBrotli(ReadOnlySpan<byte> data)
    {
        using var output = new MemoryStream();
        using (var brotli = new BrotliStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            brotli.Write(data);
        }

        return CopyToPool(output);
    }

    /// <summary>
    /// Copies the <paramref name="stream"/> contents (from position 0) into an
    /// <see cref="ArrayPool{T}"/>-rented buffer. Uses <see cref="MemoryStream.GetBuffer"/>
    /// to avoid the extra <see cref="MemoryStream.ToArray"/> allocation.
    /// </summary>
    private static (byte[] Buffer, int Length) CopyToPool(MemoryStream stream)
    {
        var len = (int)stream.Length;
        var buf = ArrayPool<byte>.Shared.Rent(len);
        stream.GetBuffer().AsSpan(0, len).CopyTo(buf);
        return (buf, len);
    }
}
