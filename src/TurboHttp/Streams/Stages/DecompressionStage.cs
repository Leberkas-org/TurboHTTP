using System;
using System.Buffers;
using System.Net.Http;
using Akka.Streams;
using Akka.Streams.Stage;
using TurboHttp.Protocol;

namespace TurboHttp.Streams.Stages;

/// <summary>
/// Decompresses HTTP response bodies according to the Content-Encoding header (RFC 9110 §8.4).
/// Handles gzip, x-gzip, deflate, and br (Brotli) encodings.
/// Responses with no Content-Encoding or "identity" pass through unchanged.
/// After decompression the Content-Encoding header is removed and Content-Length is updated.
/// </summary>
internal sealed class DecompressionStage : GraphStage<FlowShape<HttpResponseMessage, HttpResponseMessage>>
{
    private readonly Inlet<HttpResponseMessage> _inlet = new("decompression.in");
    private readonly Outlet<HttpResponseMessage> _outlet = new("decompression.out");

    public override FlowShape<HttpResponseMessage, HttpResponseMessage> Shape { get; }

    public DecompressionStage()
    {
        Shape = new FlowShape<HttpResponseMessage, HttpResponseMessage>(_inlet, _outlet);
    }

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        => new Logic(this);

    private sealed class Logic : GraphStageLogic
    {
        public Logic(DecompressionStage stage) : base(stage.Shape)
        {
            SetHandler(stage._inlet,
                onPush: () =>
                {
                    var response = Grab(stage._inlet);
                    Push(stage._outlet, Decompress(response));
                },
                onUpstreamFinish: CompleteStage,
                onUpstreamFailure: FailStage);

            SetHandler(stage._outlet,
                onPull: () => Pull(stage._inlet),
                onDownstreamFinish: _ => CompleteStage());
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

                // Copy all content headers except Content-Encoding
                foreach (var header in response.Content.Headers)
                {
                    if (header.Key.Equals("Content-Encoding", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    newContent.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }

                // Update Content-Length to reflect decompressed size
                newContent.Headers.ContentLength = decompressed.Length;

                response.Content = newContent;
                return response;
            }
            finally
            {
                owner.Dispose();
            }
        }

        /// <summary>
        /// Reads the HTTP content into a pooled buffer without heap allocation.
        /// The caller must dispose the returned <see cref="IMemoryOwner{T}"/> when done.
        /// </summary>
        private static (IMemoryOwner<byte>, int) ReadContentAsMemory(HttpContent content)
        {
            using var stream = content.ReadAsStream();

            // Fast path: seekable stream — exact size is known upfront
            if (stream.CanSeek)
            {
                var length = (int)stream.Length;
                var owner = MemoryPool<byte>.Shared.Rent(length);
                stream.ReadExactly(owner.Memory.Span[..length]);
                return (owner, length);
            }

            // Slow path: unknown size — grow a pooled buffer as needed
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

                    // Double the buffer via the pool
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