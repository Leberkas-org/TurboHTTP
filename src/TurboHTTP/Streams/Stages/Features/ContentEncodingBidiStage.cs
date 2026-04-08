using System.Buffers;
using System.IO.Compression;
using System.Net;
using Akka.Event;
using Akka.Streams;
using Akka.Streams.Stage;
using TurboHTTP.Internal;
using TurboHTTP.Protocol;
using TurboHTTP.Protocol.Semantics;

namespace TurboHTTP.Streams.Stages.Features;

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
                using var compressedStream = ContentEncodingEncoder.Compress(owner.Memory[..written].Span, policy.Encoding);
                var (compOwner, compLen) = ReadStreamToMemory(compressedStream);

                var newContent = new PooledMemoryContent(compOwner, compLen);

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
                    using var decompressedStream = ContentEncodingDecoder.Decompress(
                        owner.Memory[..written].Span, encoding);
                    var (decOwner, decLen) = ReadStreamToMemory(decompressedStream);

                    newContent = new PooledMemoryContent(decOwner, decLen);
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
        /// Reads all bytes from <paramref name="stream"/> into a
        /// <see cref="MemoryPool{T}"/>-rented buffer, growing it as needed.
        /// </summary>
        private static (IMemoryOwner<byte> Owner, int Length) ReadStreamToMemory(Stream stream)
        {
            var estimatedSize = stream.CanSeek ? Math.Max((int)stream.Length, 256) : 4096;
            var pooled = MemoryPool<byte>.Shared.Rent(estimatedSize);
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
    /// An <see cref="HttpContent"/> backed by a pooled <see cref="IMemoryOwner{T}"/>.
    /// Returns the memory to the pool on dispose, avoiding a GC allocation for the
    /// compressed or decompressed body.
    /// </summary>
    private sealed class PooledMemoryContent : HttpContent
    {
        private IMemoryOwner<byte>? _owner;
        private readonly int _length;

        public PooledMemoryContent(IMemoryOwner<byte> owner, int length)
        {
            _owner = owner;
            _length = length;
        }

        protected override void SerializeToStream(Stream stream, TransportContext? context, CancellationToken cancellationToken)
            => stream.Write(_owner!.Memory.Span[.._length]);

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            var vt = stream.WriteAsync(_owner!.Memory[.._length]);
            return vt.IsCompletedSuccessfully ? Task.CompletedTask : vt.AsTask();
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken ct)
        {
            var vt = stream.WriteAsync(_owner!.Memory[.._length], ct);
            return vt.IsCompletedSuccessfully ? Task.CompletedTask : vt.AsTask();
        }

        protected override bool TryComputeLength(out long length)
        {
            length = _length;
            return true;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                var owner = Interlocked.Exchange(ref _owner, null);
                owner?.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
