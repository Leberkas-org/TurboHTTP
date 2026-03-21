using System;
using System.Buffers;
using System.Net.Http;
using Akka.Event;
using Akka.Streams;
using Akka.Streams.Stage;
using TurboHttp.Protocol;
using TurboHttp.Protocol.RFC9110;

namespace TurboHttp.Streams.Stages.Features;

/// <summary>
/// Bidirectional stage that passes requests through unchanged and decompresses
/// response bodies according to the Content-Encoding header (RFC 9110 §8.4).
/// Handles gzip, x-gzip, deflate, and br (Brotli) encodings.
/// Responses with no Content-Encoding or "identity" pass through unchanged.
/// After decompression the Content-Encoding header is removed and Content-Length is updated.
/// </summary>
internal sealed class DecompressionBidiStage
    : GraphStage<BidiShape<HttpRequestMessage, HttpRequestMessage, HttpResponseMessage, HttpResponseMessage>>
{
    private readonly Inlet<HttpRequestMessage> _inRequest = new("Decompression.In.Request");
    private readonly Outlet<HttpRequestMessage> _outRequest = new("Decompression.Out.Request");
    private readonly Inlet<HttpResponseMessage> _inResponse = new("Decompression.In.Response");
    private readonly Outlet<HttpResponseMessage> _outResponse = new("Decompression.Out.Response");

    public override BidiShape<HttpRequestMessage, HttpRequestMessage, HttpResponseMessage, HttpResponseMessage> Shape { get; }

    public DecompressionBidiStage()
    {
        Shape = new BidiShape<HttpRequestMessage, HttpRequestMessage, HttpResponseMessage, HttpResponseMessage>(
            _inRequest, _outRequest, _inResponse, _outResponse);
    }

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        => new Logic(this);

    private sealed class Logic : GraphStageLogic
    {
        public Logic(DecompressionBidiStage stage) : base(stage.Shape)
        {
            // Request direction: pass-through
            SetHandler(stage._inRequest,
                onPush: () => Push(stage._outRequest, Grab(stage._inRequest)),
                onUpstreamFinish: () => Complete(stage._outRequest),
                onUpstreamFailure: ex => Log.Warning("DecompressionBidiStage: Request upstream failure absorbed: {0}", ex.Message));

            SetHandler(stage._outRequest,
                onPull: () => Pull(stage._inRequest),
                onDownstreamFinish: _ => Cancel(stage._inRequest));

            // Response direction: decompress
            SetHandler(stage._inResponse,
                onPush: () =>
                {
                    var response = Grab(stage._inResponse);
                    Push(stage._outResponse, Decompress(response));
                },
                onUpstreamFinish: () => Complete(stage._outResponse),
                onUpstreamFailure: ex => Log.Warning("DecompressionBidiStage: Response upstream failure absorbed: {0}", ex.Message));

            SetHandler(stage._outResponse,
                onPull: () => Pull(stage._inResponse),
                onDownstreamFinish: _ => Cancel(stage._inResponse));
        }

        private static HttpResponseMessage Decompress(HttpResponseMessage response)
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
