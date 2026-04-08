using System.IO.Compression;
using TurboHTTP.Internal;

namespace TurboHTTP.Protocol.Semantics;

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
    /// Returns a readable <see cref="Stream"/> containing the decompressed bytes.
    /// Caller MUST dispose the returned stream after use.
    /// For stacked encodings, decompression streams are chained (outermost decoded first).
    /// </summary>
    public static Stream Decompress(ReadOnlySpan<byte> body, string? contentEncoding)
    {
        if (string.IsNullOrWhiteSpace(contentEncoding))
        {
            return body.IsEmpty ? Stream.Null : SpanToStream(body);
        }

        // RFC 9110 §8.4: Content-Encoding is a comma-separated list.
        // Decodings are applied in reverse order (last encoding is outermost).
        var encodings = contentEncoding.Split(',');

        // Collect active encoding tokens in decode order (reverse of listed order).
        var activeCount = 0;
        for (var i = encodings.Length - 1; i >= 0; i--)
        {
            var enc = encodings[i].Trim();
            if (!string.IsNullOrEmpty(enc) &&
                !enc.Equals(WellKnownHeaders.Identity, StringComparison.OrdinalIgnoreCase))
            {
                activeCount++;
            }
        }

        if (activeCount == 0)
        {
            return body.IsEmpty ? Stream.Null : SpanToStream(body);
        }

        Stream current = SpanToStream(body);

        for (var i = encodings.Length - 1; i >= 0; i--)
        {
            var encoding = encodings[i].Trim();

            if (string.IsNullOrEmpty(encoding) ||
                encoding.Equals(WellKnownHeaders.Identity, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // For deflate with a seekable source, use buffered fallback (ZLib → raw DEFLATE).
            // Other encodings wrap the current stream in a decompression stream for incremental reading.
            if (encoding.Equals(WellKnownHeaders.Deflate, StringComparison.OrdinalIgnoreCase) &&
                current.CanSeek)
            {
                current = DecompressDeflateWithFallback(current);
            }
            else
            {
                current = CreateDecompressor(current, encoding);
            }
        }

        return current;
    }

    private static bool IsSupportedToken(string token)
    {
        return token.Equals(WellKnownHeaders.Gzip, StringComparison.OrdinalIgnoreCase) ||
               token.Equals(WellKnownHeaders.XGzip, StringComparison.OrdinalIgnoreCase) ||
               token.Equals(WellKnownHeaders.Deflate, StringComparison.OrdinalIgnoreCase) ||
               token.Equals(WellKnownHeaders.Brotli, StringComparison.OrdinalIgnoreCase);
    }

    private static Stream CreateDecompressor(Stream source, string encoding)
    {
        if (encoding.Equals(WellKnownHeaders.Gzip, StringComparison.OrdinalIgnoreCase) ||
            encoding.Equals(WellKnownHeaders.XGzip, StringComparison.OrdinalIgnoreCase))
        {
            return new GZipStream(source, CompressionMode.Decompress);
        }

        if (encoding.Equals(WellKnownHeaders.Brotli, StringComparison.OrdinalIgnoreCase))
        {
            return new BrotliStream(source, CompressionMode.Decompress);
        }

        if (encoding.Equals(WellKnownHeaders.Deflate, StringComparison.OrdinalIgnoreCase))
        {
            return new ZLibStream(source, CompressionMode.Decompress);
        }

        // Unknown encoding: RFC 9110 §8.4 — client cannot process unknown response encoding.
        throw new HttpDecoderException(HttpDecoderError.DecompressionFailed,
            $"RFC 9110 §8.4: Unknown Content-Encoding '{encoding}'; cannot decompress response.");
    }

    /// <summary>
    /// Decompresses deflate data with ZLib → raw DEFLATE fallback.
    /// Requires a seekable source stream. Returns a buffered <see cref="Stream"/>
    /// (RecyclableMemoryStream) positioned at 0.
    /// </summary>
    private static Stream DecompressDeflateWithFallback(Stream seekableSource)
    {
        var pos = seekableSource.Position;
        try
        {
            using var zlib = new ZLibStream(seekableSource, CompressionMode.Decompress, leaveOpen: true);
            var result = RecyclableStreams.Manager.GetStream();
            zlib.CopyTo(result);
            seekableSource.Dispose();
            result.Position = 0;
            return result;
        }
        catch
        {
            seekableSource.Position = pos;
            using var deflate = new DeflateStream(seekableSource, CompressionMode.Decompress, leaveOpen: true);
            var result = RecyclableStreams.Manager.GetStream();
            deflate.CopyTo(result);
            seekableSource.Dispose();
            result.Position = 0;
            return result;
        }
    }

    private static MemoryStream SpanToStream(ReadOnlySpan<byte> data)
    {
        var ms = RecyclableStreams.Manager.GetStream();
        ms.Write(data);
        ms.Position = 0;
        return ms;
    }
}
