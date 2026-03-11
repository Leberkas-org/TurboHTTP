using System.Buffers;
using System.Text;
using Akka.Streams.Dsl;
using TurboHttp.Protocol;
using TurboHttp.Streams.Stages;

namespace TurboHttp.StreamTests.Stages;

/// <summary>
/// Tests encoder and decoder stage lifecycle and termination behaviour.
/// Covers: upstream finish, downstream cancel, encoder exception, decoder exception.
/// </summary>
public sealed class StageLifecycleTests : StreamTestBase
{
    // ── Helpers ──────────────────────────────────────────────────────────────────

    private static HttpRequestMessage ValidRequest()
        => new(HttpMethod.Get, "http://example.com/")
        {
            Version = System.Net.HttpVersion.Version11
        };

    private static (IMemoryOwner<byte>, int) Chunk(string ascii)
    {
        var bytes = Encoding.Latin1.GetBytes(ascii);
        return (new SimpleMemoryOwner(bytes), bytes.Length);
    }

    private static Exception Unwrap(Exception ex)
        => ex is AggregateException agg ? agg.InnerException! : ex;

    // ── LIFE-001 ──────────────────────────────────────────────────────────────────

    [Fact(Timeout = 10_000,
        DisplayName = "LIFE-001: UpstreamFinish → encoder stage completes without exception")]
    public async Task LIFE_001_EncoderStage_UpstreamFinish_CompletesCleanly()
    {
        // An empty source completes immediately (upstream finish).
        // The encoder stage must propagate the completion signal without throwing.
        var results = await Source.Empty<HttpRequestMessage>()
            .Via(Flow.FromGraph(new Http11EncoderStage()))
            .RunWith(Sink.Seq<(IMemoryOwner<byte>, int)>(), Materializer);

        // Empty input → no output → stage completed cleanly
        Assert.Empty(results);
    }

    [Fact(Timeout = 10_000,
        DisplayName = "LIFE-001b: UpstreamFinish → decoder stage completes without exception")]
    public async Task LIFE_001b_DecoderStage_UpstreamFinish_CompletesCleanly()
    {
        // An empty source completes immediately (upstream finish).
        // The decoder stage must propagate completion cleanly.
        var results = await Source.Empty<(IMemoryOwner<byte>, int)>()
            .Via(Flow.FromGraph(new Http11DecoderStage()))
            .RunWith(Sink.Seq<HttpResponseMessage>(), Materializer);

        Assert.Empty(results);
    }

    // ── LIFE-002 ──────────────────────────────────────────────────────────────────

    [Fact(Timeout = 10_000,
        DisplayName = "LIFE-002: DownstreamCancel → encoder stage shuts down cleanly")]
    public async Task LIFE_002_EncoderStage_DownstreamCancel_ShutsDownCleanly()
    {
        // Source.Repeat produces an infinite stream. Sink.First takes exactly one element
        // and then cancels downstream, triggering onDownstreamFinish on the stage.
        // The stage must call CompleteStage() — no exception must escape.
        var result = await Source.Repeat(ValidRequest())
            .Via(Flow.FromGraph(new Http11EncoderStage()))
            .RunWith(Sink.First<(IMemoryOwner<byte>, int)>(), Materializer);

        var (owner, written) = result;
        try
        {
            // Confirm a real response was emitted before cancel
            Assert.True(written > 0, "Expected at least one byte before downstream cancel");
        }
        finally
        {
            owner.Dispose();
        }
    }

    [Fact(Timeout = 10_000,
        DisplayName = "LIFE-002b: DownstreamCancel → decoder stage shuts down cleanly")]
    public async Task LIFE_002b_DecoderStage_DownstreamCancel_ShutsDownCleanly()
    {
        // Feed a valid HTTP/1.1 response into the decoder; Sink.First cancels after first message.
        // The decoder stage must not throw when downstream cancels.
        const string rawResponse = "HTTP/1.1 200 OK\r\nContent-Length: 2\r\n\r\nOK";
        var fragments = new List<(IMemoryOwner<byte>, int)> { Chunk(rawResponse) };

        var response = await Source.From(fragments)
            .Via(Flow.FromGraph(new Http11DecoderStage()))
            .RunWith(Sink.First<HttpResponseMessage>(), Materializer);

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
    }

    // ── LIFE-003 ──────────────────────────────────────────────────────────────────

    [Fact(Timeout = 10_000,
        DisplayName = "LIFE-003: Exception in encoder → stage fails with meaningful error message")]
    public async Task LIFE_003_EncoderStage_InvalidRequest_FailsWithMeaningfulMessage()
    {
        // A request with null RequestUri causes Http11Encoder to throw ArgumentNullException.
        // The encoder stage catches the exception, calls FailStage(), and the stream fails.
        // The exception must have a non-empty message (meaningful error).
        var badRequest = new HttpRequestMessage
        {
            Method = HttpMethod.Get,
            RequestUri = null   // ← null URI triggers ArgumentNullException in the encoder
        };

        var ex = await Assert.ThrowsAnyAsync<Exception>(async () =>
            await Source.Single(badRequest)
                .Via(Flow.FromGraph(new Http11EncoderStage()))
                .RunWith(Sink.Seq<(IMemoryOwner<byte>, int)>(), Materializer));

        var inner = Unwrap(ex);

        // The stage must propagate a real exception (not a generic one)
        Assert.NotNull(inner);
        Assert.False(string.IsNullOrWhiteSpace(inner.Message),
            "Exception must carry a meaningful, non-empty error message");
    }

    // ── LIFE-004 ──────────────────────────────────────────────────────────────────

    [Fact(Timeout = 10_000,
        DisplayName = "LIFE-004: Exception in decoder → stage fails with HttpDecoderException")]
    public async Task LIFE_004_DecoderStage_MalformedData_FailsWithHttpDecoderException()
    {
        // Sending bytes that are not a valid HTTP response (no "HTTP/1.x" status-line)
        // must cause Http11Decoder to throw HttpDecoderException(InvalidStatusLine).
        // The decoder stage catches it, calls FailStage(), and the stream fails.
        // We verify the inner exception is exactly HttpDecoderException.
        var garbage = Chunk("GARBAGE DATA\r\n\r\n");

        var ex = await Assert.ThrowsAnyAsync<Exception>(async () =>
            await Source.Single(garbage)
                .Via(Flow.FromGraph(new Http11DecoderStage()))
                .RunWith(Sink.First<HttpResponseMessage>(), Materializer));

        var inner = Unwrap(ex);

        Assert.IsType<HttpDecoderException>(inner);

        var decoderEx = (HttpDecoderException)inner;
        Assert.Equal(HttpDecodeError.InvalidStatusLine, decoderEx.DecodeError);
    }
}
