using System.Buffers;
using Akka.Event;
using Akka.Streams;
using Akka.Streams.Stage;
using TurboHttp.Protocol;
using TurboHttp.Protocol.RFC9110;

namespace TurboHttp.Streams.Stages.Features;

/// <summary>
/// Bidirectional stage that handles both request body compression and response body
/// decompression for Content-Encoding (RFC 9110 §8.4).
/// <para>
/// <b>Request direction (In1→Out1):</b> When a <see cref="RequestCompressionPolicy"/> is
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
    private readonly RequestCompressionPolicy? _compressionPolicy;

    private readonly Inlet<HttpRequestMessage> _inRequest = new("ContentEncoding.In.Request");
    private readonly Outlet<HttpRequestMessage> _outRequest = new("ContentEncoding.Out.Request");
    private readonly Inlet<HttpResponseMessage> _inResponse = new("ContentEncoding.In.Response");
    private readonly Outlet<HttpResponseMessage> _outResponse = new("ContentEncoding.Out.Response");

    public override BidiShape<HttpRequestMessage, HttpRequestMessage, HttpResponseMessage, HttpResponseMessage> Shape { get; }

    public ContentEncodingBidiStage(
        bool automaticDecompression = true,
        RequestCompressionPolicy? compressionPolicy = null)
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

        private static HttpRequestMessage CompressIfNeeded(HttpRequestMessage request, RequestCompressionPolicy policy)
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
                var compressed = ContentEncodingEncoder.Compress(owner.Memory[..written].ToArray(), policy.Encoding);

                var newContent = new ByteArrayContent(compressed);

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
                newContent.Headers.ContentLength = compressed.Length;

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

            var (owner, written) = ReadContentAsMemory(response.Content);
            try
            {
                var decompressed = ContentEncodingDecoder.Decompress(owner.Memory[..written].ToArray(), encoding);

                var newContent = new ByteArrayContent(decompressed);

                foreach (var header in response.Content.Headers)
                {
                    if (header.Key.Equals("Content-Encoding", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    newContent.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }

                newContent.Headers.ContentLength = decompressed.Length;

                response.Content = newContent;
                return response;
            }
            catch (HttpDecoderException)
            {
                // Decompression failure on a supported encoding — pass the response
                // through unmodified rather than killing the stream.
                return response;
            }
            finally
            {
                owner.Dispose();
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
}
