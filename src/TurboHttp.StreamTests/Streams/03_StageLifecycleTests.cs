using System.Text;
using Akka.Streams.Dsl;
using TurboHttp.Internal;
using TurboHttp.Streams.Stages.Decoding;
using TurboHttp.Streams.Stages.Encoding;

namespace TurboHttp.StreamTests.Streams;

/// <summary>
/// Tests startup and teardown behavior of encoder and decoder stages.
/// Verifies clean completion and failure propagation during stage lifecycle events.
/// </summary>
/// <remarks>
/// Stage under test: <see cref="Http11EncoderStage"/>, <see cref="Http11DecoderStage"/>.
/// Validates upstream finish, downstream cancel, and error failure paths.
/// </remarks>
public sealed class StageLifecycleTests : StreamTestBase
{
    private static HttpRequestMessage ValidRequest()
        => new(HttpMethod.Get, "http://example.com/")
        {
            Version = System.Net.HttpVersion.Version11
        };

    private static IInputItem Chunk(string ascii)
    {
        var bytes = Encoding.Latin1.GetBytes(ascii);
        return new DataItem(new SimpleMemoryOwner(bytes), bytes.Length) { Key = RequestEndpoint.Default };
    }

    [Fact(Timeout = 10_000,
        DisplayName = "LIFE-001: UpstreamFinish → encoder stage completes without exception")]
    public async Task Should_CompleteCleanly_When_EncoderStageUpstreamFinishes()
    {
        // An empty source completes immediately (upstream finish).
        // The encoder stage must propagate the completion signal without throwing.
        var results = await Source.Empty<HttpRequestMessage>()
            .Via(Flow.FromGraph(new Http11EncoderStage()))
            .RunWith(Sink.Seq<IOutputItem>(), Materializer);

        // Empty input → no output → stage completed cleanly
        Assert.Empty(results);
    }

    [Fact(Timeout = 10_000,
        DisplayName = "LIFE-001b: UpstreamFinish → decoder stage completes without exception")]
    public async Task Should_CompleteCleanly_When_DecoderStageUpstreamFinishes()
    {
        // An empty source completes immediately (upstream finish).
        // The decoder stage must propagate completion cleanly.
        var results = await Source.Empty<IInputItem>()
            .Via(Flow.FromGraph(new Http11DecoderStage()))
            .RunWith(Sink.Seq<HttpResponseMessage>(), Materializer);

        Assert.Empty(results);
    }

    [Fact(Timeout = 10_000,
        DisplayName = "LIFE-002: DownstreamCancel → encoder stage shuts down cleanly")]
    public async Task Should_ShutDownCleanly_When_EncoderStageDownstreamCancels()
    {
        // Source.Repeat produces an infinite stream. Sink.First takes exactly one element
        // and then cancels downstream, triggering onDownstreamFinish on the stage.
        // The stage must call CompleteStage() — no exception must escape.
        var result = await Source.Repeat(ValidRequest())
            .Via(Flow.FromGraph(new Http11EncoderStage()))
            .RunWith(Sink.First<IOutputItem>(), Materializer);

        var data = (DataItem)result;
        try
        {
            // Confirm a real response was emitted before cancel
            Assert.True(data.Length > 0, "Expected at least one byte before downstream cancel");
        }
        finally
        {
            data.Memory.Dispose();
        }
    }

    [Fact(Timeout = 10_000,
        DisplayName = "LIFE-002b: DownstreamCancel → decoder stage shuts down cleanly")]
    public async Task Should_ShutDownCleanly_When_DecoderStageDownstreamCancels()
    {
        // Feed a valid HTTP/1.1 response into the decoder; Sink.First cancels after first message.
        // The decoder stage must not throw when downstream cancels.
        const string rawResponse = "HTTP/1.1 200 OK\r\nContent-Length: 2\r\n\r\nOK";
        var fragments = new List<IInputItem> { Chunk(rawResponse) };

        var response = await Source.From(fragments)
            .Via(Flow.FromGraph(new Http11DecoderStage()))
            .RunWith(Sink.First<HttpResponseMessage>(), Materializer);

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
    }

    [Fact(Timeout = 10_000,
        DisplayName = "LIFE-005: HTTP/1.1 decoder survives corrupt bytes — valid response after garbage is decoded")]
    public async Task Should_DecodeValidResponse_When_CorruptBytesReceivedFirst()
    {
        // Corrupt bytes cause HttpDecoderException in the decoder.
        // The stage must log the error, reset state, and continue pulling.
        // The subsequent valid response must be decoded successfully.
        var garbage = Chunk("GARBAGE DATA\r\n\r\n");
        var valid = Chunk("HTTP/1.1 200 OK\r\nContent-Length: 0\r\n\r\n");

        var response = await Source.From([garbage, valid])
            .Via(Flow.FromGraph(new Http11DecoderStage()))
            .RunWith(Sink.First<HttpResponseMessage>(), Materializer);

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
    }

    [Fact(Timeout = 10_000,
        DisplayName = "LIFE-006: HTTP/1.0 decoder survives corrupt bytes — valid response after garbage is decoded")]
    public async Task Should_DecodeValidHttp10Response_When_CorruptBytesReceivedFirst()
    {
        // Corrupt bytes cause HttpDecoderException in the HTTP/1.0 decoder.
        // The stage must log the error, reset state, and continue pulling.
        // The subsequent valid HTTP/1.0 response must be decoded successfully.
        var garbage = Chunk("GARBAGE DATA\r\n\r\n");
        var valid = Chunk("HTTP/1.0 200 OK\r\nContent-Length: 0\r\n\r\n");

        var response = await Source.From([garbage, valid])
            .Via(Flow.FromGraph(new Http10DecoderStage()))
            .RunWith(Sink.First<HttpResponseMessage>(), Materializer);

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
    }

}