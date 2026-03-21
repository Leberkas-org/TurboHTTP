using System;
using System.Buffers;
using System.Net.Http;
using Akka.Event;
using Akka.Streams;
using Akka.Streams.Stage;
using TurboHttp.Protocol.RFC9110;

namespace TurboHttp.Streams.Stages.Features;

/// <summary>
/// Bidirectional stage that compresses request bodies according to a
/// <see cref="RequestCompressionPolicy"/> (RFC 9110 §8.4).
/// <para>
/// <b>Request direction (In1→Out1):</b> Requests with a body at or above the
/// <see cref="RequestCompressionPolicy.MinBodySizeBytes"/> threshold are compressed using
/// the configured encoding. The Content-Encoding header is set and Content-Length is updated.
/// Requests with no body or a body below the threshold pass through unchanged.
/// </para>
/// <para>
/// <b>Response direction (In2→Out2):</b> Pure pass-through.
/// </para>
/// <para>
/// When no <see cref="RequestCompressionPolicy"/> is provided the stage is a pass-through
/// in both directions.
/// </para>
/// </summary>
internal sealed class RequestCompressionBidiStage
    : GraphStage<BidiShape<HttpRequestMessage, HttpRequestMessage, HttpResponseMessage, HttpResponseMessage>>
{
    private readonly RequestCompressionPolicy? _policy;

    private readonly Inlet<HttpRequestMessage> _inRequest = new("RequestCompression.In.Request");
    private readonly Outlet<HttpRequestMessage> _outRequest = new("RequestCompression.Out.Request");
    private readonly Inlet<HttpResponseMessage> _inResponse = new("RequestCompression.In.Response");
    private readonly Outlet<HttpResponseMessage> _outResponse = new("RequestCompression.Out.Response");

    public override BidiShape<HttpRequestMessage, HttpRequestMessage, HttpResponseMessage, HttpResponseMessage> Shape { get; }

    /// <summary>
    /// Creates a new <see cref="RequestCompressionBidiStage"/> with the given policy.
    /// </summary>
    /// <param name="policy">Compression policy. When null, the stage is a pass-through.</param>
    public RequestCompressionBidiStage(RequestCompressionPolicy? policy = null)
    {
        _policy = policy;
        Shape = new BidiShape<HttpRequestMessage, HttpRequestMessage, HttpResponseMessage, HttpResponseMessage>(
            _inRequest, _outRequest, _inResponse, _outResponse);
    }

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        => new Logic(this);

    private sealed class Logic : GraphStageLogic
    {
        public Logic(RequestCompressionBidiStage stage) : base(stage.Shape)
        {
            if (stage._policy is null)
            {
                // Null policy → pure pass-through in both directions
                SetHandler(stage._inRequest,
                    onPush: () => Push(stage._outRequest, Grab(stage._inRequest)),
                    onUpstreamFinish: () => Complete(stage._outRequest),
                    onUpstreamFailure: ex => Log.Warning("RequestCompressionBidiStage: Request upstream failure absorbed: {0}", ex.Message));

                SetHandler(stage._outRequest,
                    onPull: () => Pull(stage._inRequest),
                    onDownstreamFinish: _ => Cancel(stage._inRequest));

                SetHandler(stage._inResponse,
                    onPush: () => Push(stage._outResponse, Grab(stage._inResponse)),
                    onUpstreamFinish: () => Complete(stage._outResponse),
                    onUpstreamFailure: ex => Log.Warning("RequestCompressionBidiStage: Response upstream failure absorbed: {0}", ex.Message));

                SetHandler(stage._outResponse,
                    onPull: () => Pull(stage._inResponse),
                    onDownstreamFinish: _ => Cancel(stage._inResponse));

                return;
            }

            // --- Request direction (In1→Out1): compress ---
            SetHandler(stage._inRequest,
                onPush: () =>
                {
                    var request = Grab(stage._inRequest);
                    Push(stage._outRequest, CompressIfNeeded(request, stage._policy));
                },
                onUpstreamFinish: () => Complete(stage._outRequest),
                onUpstreamFailure: ex => Log.Warning("RequestCompressionBidiStage: Request upstream failure absorbed: {0}", ex.Message));

            SetHandler(stage._outRequest,
                onPull: () => Pull(stage._inRequest),
                onDownstreamFinish: _ => Cancel(stage._inRequest));

            // --- Response direction (In2→Out2): pass-through ---
            SetHandler(stage._inResponse,
                onPush: () => Push(stage._outResponse, Grab(stage._inResponse)),
                onUpstreamFinish: () => Complete(stage._outResponse),
                onUpstreamFailure: ex => Log.Warning("RequestCompressionBidiStage: Response upstream failure absorbed: {0}", ex.Message));

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
