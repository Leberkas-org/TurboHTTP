using System.Buffers;
using System.IO.Compression;
using System.Net;
using Akka.Event;
using Akka.Streams;
using Akka.Streams.Stage;
using TurboHttp.Protocol;
using TurboHttp.Protocol.Semantics;

namespace TurboHttp.Streams.Stages.Features;

/// <summary>
/// Bidirectional stage that handles both request body compression and response body
/// decompression for Content-Encoding (RFC 9110 §8.4).
/// <para>
/// <b>Request direction (In1→Out1):</b> When a <see cref="CompressionPolicy"/> is
/// provided, requests with a body at or above the threshold are compressed. Otherwise
/// requests pass through unchanged.
/// </para>
/// <para>
/// <b>Response direction (In2→Out2):</b> When a <see cref="bool"/> is
/// true, responses with a Content-Encoding header (gzip, x-gzip, deflate, br) are
/// decompressed, the header is removed, and Content-Length is updated. Otherwise responses
/// pass through unchanged.
/// </para>
/// </summary>
internal sealed class ContentEncodingBidiStage
    : GraphStage<BidiShape<HttpRequestMessage, HttpRequestMessage, HttpResponseMessage, HttpResponseMessage>>
{
    private readonly bool _automaticDecompression;
    private readonly CompressionPolicy? _compressionPolicy;

    private readonly Inlet<HttpRequestMessage> _inRequest = new("ContentEncoding.In.Request");
    private readonly Outlet<HttpRequestMessage> _outRequest = new("ContentEncoding.Out.Request");
    private readonly Inlet<HttpResponseMessage> _inResponse = new("ContentEncoding.In.Response");
    private readonly Outlet<HttpResponseMessage> _outResponse = new("ContentEncoding.Out.Response");

    public override BidiShape<HttpRequestMessage, HttpRequestMessage, HttpResponseMessage, HttpResponseMessage> Shape
    {
        get;
    }

    public ContentEncodingBidiStage(
        bool automaticDecompression = true,
        CompressionPolicy? compressionPolicy = null)
    {
        _automaticDecompression = automaticDecompression;
        _compressionPolicy = compressionPolicy;
        Shape = new BidiShape<HttpRequestMessage, HttpRequestMessage, HttpResponseMessage, HttpResponseMessage>(
            _inRequest, _outRequest, _inResponse, _outResponse);
    }

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        => new Logic(this);

    private sealed class Logic : GraphStageLogic
    {
        public Logic(ContentEncodingBidiStage stage) : base(stage.Shape)
        {
            // --- Request direction (In1→Out1) ---
            if (stage._compressionPolicy is not null)
            {
                var policy = stage._compressionPolicy;
                SetHandler(stage._inRequest,
                    onPush: () =>
                    {
                        var request = Grab(stage._inRequest);
                        Push(stage._outRequest, CompressIfNeeded(request, policy));
                    },
                    onUpstreamFinish: () => Complete(stage._outRequest),
                    onUpstreamFailure: ex =>
                    {
                        Log.Warning("ContentEncodingBidiStage: Request upstream failure absorbed: {0}", ex.Message);
                        Complete(stage._outRequest);
                    });
            }
            else
            {
                SetHandler(stage._inRequest,
                    onPush: () => Push(stage._outRequest, Grab(stage._inRequest)),
                    onUpstreamFinish: () => Complete(stage._outRequest),
                    onUpstreamFailure: ex =>
                    {
                        Log.Warning("ContentEncodingBidiStage: Request upstream failure absorbed: {0}", ex.Message);
                        Complete(stage._outRequest);
                    });
            }

            SetHandler(stage._outRequest,
                onPull: () => Pull(stage._inRequest),
                onDownstreamFinish: _ => Cancel(stage._inRequest));

            // --- Response direction (In2→Out2) ---
            if (stage._automaticDecompression)
            {
                SetHandler(stage._inResponse,
                    onPush: () =>
                    {
                        var response = Grab(stage._inResponse);
                        Push(stage._outResponse, Decompress(response));
                    },
                    onUpstreamFinish: () => Complete(stage._outResponse),
                    onUpstreamFailure: ex =>
                    {
                        Log.Warning("ContentEncodingBidiStage: Response upstream failure absorbed: {0}", ex.Message);
                        Complete(stage._outResponse);
                    });
            }
            else
            {
                SetHandler(stage._inResponse,
                    onPush: () => Push(stage._outResponse, Grab(stage._inResponse)),
                    onUpstreamFinish: () => Complete(stage._outResponse),
                    onUpstreamFailure: ex =>
                    {
                        Log.Warning("ContentEncodingBidiStage: Response upstream failure absorbed: {0}", ex.Message);
                        Complete(stage._outResponse);
                    });
            }

            SetHandler(stage._outResponse,
                onPull: () => Pull(stage._inResponse),
                onDownstreamFinish: _ => Cancel(stage._inResponse));
        }

        private static HttpRequestMessage CompressIfNeeded(HttpRequestMessage request, CompressionPolicy policy)
        {
            if (request.Content is null)
            {
                return request;
            }

            var bodySize = request.Content.Headers.ContentLength ?? -1;

            if (bodySize < policy.MinBodySizeBytes)
            {
                return request;
            }

            var (owner, written) = ReadContentAsMemory(request.Content);
            try
            {
                var (compBuf, compLen) = ContentEncodingEncoder.Compress(owner.Memory[..written].Span, policy.Encoding);

                var newContent = new PooledArrayContent(compBuf, compLen);

                // Copy existing content headers (except Content-Encoding and Content-Length)
                foreach (var header in request.Content.Headers)
                {
                    if (header.Key.Equals("Content-Encoding", StringComparison.OrdinalIgnoreCase) ||
                        header.Key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    newContent.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }

                // Set Content-Encoding and update Content-Length
                newContent.Headers.TryAddWithoutValidation("Content-Encoding", policy.Encoding);
                newContent.Headers.ContentLength = compLen;

                request.Content = newContent;
                return request;
            }
            finally
            {
                owner.Dispose();
            }
        }

        private HttpResponseMessage Decompress(HttpResponseMessage response)
        {
            if (!response.Content.Headers.TryGetValues("Content-Encoding", out var values))
            {
                return response;
            }

            var encoding = string.Join(", ", values).Trim();

            if (string.IsNullOrEmpty(encoding) ||
                encoding.Equals(WellKnownHeaders.Identity, StringComparison.OrdinalIgnoreCase))
            {
                return response;
            }

            // Unknown encoding: pass the response through unchanged rather than
            // allocating buffers and throwing. The caller sees the raw body,
            // which is the correct fallback per RFC 9110 §8.4.
            if (!ContentEncodingDecoder.IsSupported(encoding))
            {
                Log.Debug("ContentEncodingBidiStage: unknown encoding '{0}', passing through unchanged", encoding);
                return response;
            }

            HttpContent newContent;

            // Streaming decompression for gzip/brotli (single encoding) when the body is
            // large enough to justify the streaming overhead. For small or unknown bodies
            // the buffered path is used — it can catch corrupt data and fall back to raw passthrough.
            // Deflate always uses the buffered path because it needs a ZLib→raw DEFLATE fallback
            // that requires a seekable stream.
            const long streamingThreshold = 64 * 1024;
            var contentLength = response.Content.Headers.ContentLength;
            var canStream = !encoding.Contains(',') &&
                            !encoding.Equals(WellKnownHeaders.Deflate, StringComparison.OrdinalIgnoreCase) &&
                            contentLength > streamingThreshold;

            if (canStream)
            {
                // Large-body streaming path: wrap the original content stream in a
                // decompression stream. The body is decompressed lazily on read,
                // avoiding a full buffering + copy cycle.
                newContent = new DecompressingContent(response.Content, encoding);
            }
            else
            {
                // Buffered path: small bodies, deflate (ZLib/raw fallback), stacked encodings,
                // and unknown Content-Length. Errors are caught and the raw response is passed through.
                var (owner, written) = ReadContentAsMemory(response.Content);
                try
                {
                    (byte[] Buffer, int Length) result;
                    if (!encoding.Contains(','))
                    {
                        result = DecompressToPool(owner.Memory[..written].Span, encoding);
                    }
                    else
                    {
                        result = ContentEncodingDecoder.Decompress(owner.Memory[..written].Span, encoding);
                    }

                    newContent = new PooledArrayContent(result.Buffer, result.Length);
                }
                catch (Exception ex) when (ex is HttpDecoderException or InvalidDataException or InvalidOperationException)
                {
                    owner.Dispose();
                    Log.Warning("ContentEncodingBidiStage: decompression failed ({0}), passing raw response through", ex.Message);
                    return response;
                }
                finally
                {
                    owner.Dispose();
                }
            }

            foreach (var header in response.Content.Headers)
            {
                if (header.Key.Equals("Content-Encoding", StringComparison.OrdinalIgnoreCase) ||
                    header.Key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                newContent.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            response.Content = newContent;
            return response;
        }

        /// <summary>
        /// Decompresses <paramref name="data"/> using a single encoding into an
        /// <see cref="ArrayPool{T}"/>-rented buffer. The caller must ensure the
        /// returned buffer is returned to the pool (typically via <see cref="PooledArrayContent"/>).
        /// </summary>
        private static (byte[] Buffer, int Length) DecompressToPool(ReadOnlySpan<byte> data, string encoding)
        {
            if (data.IsEmpty)
            {
                return ([], 0);
            }

            // Conservative estimate: 4× compressed size, floor 4 KB.
            var estimatedSize = Math.Max(4096, data.Length * 4);

            if (encoding.Equals(WellKnownHeaders.Gzip, StringComparison.OrdinalIgnoreCase) ||
                encoding.Equals(WellKnownHeaders.XGzip, StringComparison.OrdinalIgnoreCase))
            {
                return DecompressGzipToPool(data, estimatedSize);
            }

            if (encoding.Equals(WellKnownHeaders.Deflate, StringComparison.OrdinalIgnoreCase))
            {
                return DecompressDeflateToPool(data, estimatedSize);
            }

            if (encoding.Equals(WellKnownHeaders.Brotli, StringComparison.OrdinalIgnoreCase))
            {
                return DecompressBrotliToPool(data, estimatedSize);
            }

            throw new HttpDecoderException(HttpDecoderError.DecompressionFailed,
                $"RFC 9110 §8.4: Unknown Content-Encoding '{encoding}'; cannot decompress response.");
        }

        private static (byte[] Buffer, int Length) DecompressGzipToPool(ReadOnlySpan<byte> data, int estimatedSize)
        {
            using var input = SpanToMemoryStream(data);
            using var gzip = new GZipStream(input, CompressionMode.Decompress);
            return ReadDecompressorToPool(gzip, estimatedSize);
        }

        private static (byte[] Buffer, int Length) DecompressBrotliToPool(ReadOnlySpan<byte> data, int estimatedSize)
        {
            using var input = SpanToMemoryStream(data);
            using var brotli = new BrotliStream(input, CompressionMode.Decompress);
            return ReadDecompressorToPool(brotli, estimatedSize);
        }

        private static (byte[] Buffer, int Length) DecompressDeflateToPool(ReadOnlySpan<byte> data, int estimatedSize)
        {
            // RFC 9110 §8.4.2: "deflate" is the zlib format (RFC 1950), not raw DEFLATE.
            // However, some servers send raw DEFLATE (RFC 1951) without the zlib wrapper.
            // Try ZLib first; fall back to raw DEFLATE if it fails.
            try
            {
                using var input = SpanToMemoryStream(data);
                using var zlib = new ZLibStream(input, CompressionMode.Decompress);
                return ReadDecompressorToPool(zlib, estimatedSize);
            }
            catch
            {
                using var input = SpanToMemoryStream(data);
                using var deflate = new DeflateStream(input, CompressionMode.Decompress);
                return ReadDecompressorToPool(deflate, estimatedSize);
            }
        }

        private static MemoryStream SpanToMemoryStream(ReadOnlySpan<byte> data)
        {
            var ms = new MemoryStream(data.Length);
            ms.Write(data);
            ms.Position = 0;
            return ms;
        }

        /// <summary>
        /// Reads all bytes from <paramref name="decompressor"/> into an
        /// <see cref="ArrayPool{T}"/>-rented buffer, growing it as needed.
        /// </summary>
        private static (byte[] Buffer, int Length) ReadDecompressorToPool(Stream decompressor, int estimatedSize)
        {
            var buffer = ArrayPool<byte>.Shared.Rent(estimatedSize);
            var written = 0;

            try
            {
                while (true)
                {
                    if (written == buffer.Length)
                    {
                        var larger = ArrayPool<byte>.Shared.Rent(buffer.Length * 2);
                        buffer.AsSpan(0, written).CopyTo(larger);
                        ArrayPool<byte>.Shared.Return(buffer);
                        buffer = larger;
                    }

                    var read = decompressor.Read(buffer, written, buffer.Length - written);
                    if (read == 0)
                    {
                        break;
                    }

                    written += read;
                }

                return (buffer, written);
            }
            catch
            {
                ArrayPool<byte>.Shared.Return(buffer);
                throw;
            }
        }

        private static (IMemoryOwner<byte>, int) ReadContentAsMemory(HttpContent content)
        {
            using var stream = content.ReadAsStream();

            if (stream.CanSeek)
            {
                var length = (int)stream.Length;
                var owner = MemoryPool<byte>.Shared.Rent(length);
                stream.ReadExactly(owner.Memory.Span[..length]);
                return (owner, length);
            }

            var pooled = MemoryPool<byte>.Shared.Rent(4096);
            var written = 0;

            try
            {
                int read;
                while ((read = stream.Read(pooled.Memory.Span[written..])) > 0)
                {
                    written += read;

                    if (written < pooled.Memory.Length)
                    {
                        continue;
                    }

                    var larger = MemoryPool<byte>.Shared.Rent(pooled.Memory.Length * 2);
                    pooled.Memory.Span[..written].CopyTo(larger.Memory.Span);
                    pooled.Dispose();
                    pooled = larger;
                }

                return (pooled, written);
            }
            catch
            {
                pooled.Dispose();
                throw;
            }
        }
    }

    /// <summary>
    /// An <see cref="HttpContent"/> that lazily decompresses the underlying content on read.
    /// Avoids buffering the entire compressed body in memory — the decompression stream wraps
    /// the original content's stream and decompresses on-the-fly as downstream consumers read.
    /// </summary>
    private sealed class DecompressingContent : HttpContent
    {
        private readonly HttpContent _inner;
        private readonly string _encoding;

        public DecompressingContent(HttpContent inner, string encoding)
        {
            _inner = inner;
            _encoding = encoding;
        }

        protected override void SerializeToStream(Stream stream, TransportContext? context, CancellationToken ct)
        {
            using var source = _inner.ReadAsStream(ct);
            using var decompressor = CreateDecompressor(source, _encoding);
            decompressor.CopyTo(stream);
        }

        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            await using var source = await _inner.ReadAsStreamAsync().ConfigureAwait(false);
            await using var decompressor = CreateDecompressor(source, _encoding);
            await decompressor.CopyToAsync(stream).ConfigureAwait(false);
        }

        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken ct)
        {
            await using var source = await _inner.ReadAsStreamAsync(ct).ConfigureAwait(false);
            await using var decompressor = CreateDecompressor(source, _encoding);
            await decompressor.CopyToAsync(stream, ct).ConfigureAwait(false);
        }

        protected override bool TryComputeLength(out long length)
        {
            // Decompressed length is unknown without reading the entire stream.
            length = 0;
            return false;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
            }

            base.Dispose(disposing);
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

            throw new HttpDecoderException(HttpDecoderError.DecompressionFailed,
                $"RFC 9110 §8.4: Unknown Content-Encoding '{encoding}'.");
        }
    }

    /// <summary>
    /// An <see cref="HttpContent"/> backed by an <see cref="ArrayPool{T}"/>-rented buffer.
    /// Returns the buffer to the pool on dispose, avoiding a GC allocation for the decompressed body.
    /// </summary>
    private sealed class PooledArrayContent : HttpContent
    {
        private byte[]? _buffer;
        private readonly int _length;

        public PooledArrayContent(byte[] buffer, int length)
        {
            _buffer = buffer;
            _length = length;
        }

        protected override void SerializeToStream(Stream stream, TransportContext? context, CancellationToken cancellationToken)
            => stream.Write(_buffer!, 0, _length);

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
            => stream.WriteAsync(_buffer!, 0, _length);

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken ct)
            => stream.WriteAsync(_buffer!, 0, _length, ct);

        protected override bool TryComputeLength(out long length)
        {
            length = _length;
            return true;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _buffer is not null)
            {
                ArrayPool<byte>.Shared.Return(_buffer);
                _buffer = null;
            }

            base.Dispose(disposing);
        }
    }
}