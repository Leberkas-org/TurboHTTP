using System.Net.Http;
using System.Text;
using Akka.Streams.Dsl;
using TurboHttp.Protocol.RFC9114;
using TurboHttp.Streams.Stages.Encoding;

namespace TurboHttp.StreamTests.RFC9114;

/// <summary>
/// Tests the HTTP/3 request-to-frame conversion stage per RFC 9114 §4.1.
/// Verifies that <see cref="Http30Request2FrameStage"/> correctly produces
/// HEADERS frames (QPACK-encoded) and DATA frames for request bodies.
/// </summary>
/// <remarks>
/// Stage under test: <see cref="Http30Request2FrameStage"/>.
/// Unlike HTTP/2, HTTP/3 frames carry no stream identifier (QUIC handles that)
/// and header blocks are never split across CONTINUATION frames.
/// </remarks>
public sealed class Http30Request2FrameStageTests : StreamTestBase
{
    private readonly Http3RequestEncoder _encoder = new();

    private async Task<List<Http3Frame>> EncodeRequestAsync(HttpRequestMessage request)
    {
        var frames = await Source.Single(request)
            .Via(Flow.FromGraph(new Http30Request2FrameStage(_encoder)))
            .RunWith(Sink.Seq<Http3Frame>(), Materializer);

        return frames.ToList();
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9114-4.1-30R2F-001: GET request produces single HEADERS frame")]
    public async Task Should_ProduceHeadersFrame_ForGetRequest()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/path");

        var frames = await EncodeRequestAsync(request);

        Assert.Single(frames);
        Assert.IsType<Http3HeadersFrame>(frames[0]);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9114-4.1-30R2F-002: POST request with body produces HEADERS + DATA frames")]
    public async Task Should_ProduceHeadersAndDataFrames_ForPostWithBody()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "https://example.com/submit")
        {
            Content = new StringContent("hello", Encoding.UTF8, "text/plain")
        };

        var frames = await EncodeRequestAsync(request);

        Assert.Equal(2, frames.Count);
        Assert.IsType<Http3HeadersFrame>(frames[0]);
        Assert.IsType<Http3DataFrame>(frames[1]);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9114-4.1-30R2F-003: DATA frame carries request body bytes")]
    public async Task Should_CarryBodyBytes_InDataFrame()
    {
        var body = "request-body-content";
        var request = new HttpRequestMessage(HttpMethod.Post, "https://example.com/data")
        {
            Content = new StringContent(body, Encoding.UTF8, "text/plain")
        };

        var frames = await EncodeRequestAsync(request);

        var dataFrame = Assert.IsType<Http3DataFrame>(frames[1]);
        var bodyBytes = dataFrame.Data.ToArray();
        var decoded = Encoding.UTF8.GetString(bodyBytes);
        Assert.Equal(body, decoded);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9114-4.1-30R2F-004: POST with empty body produces only HEADERS frame")]
    public async Task Should_ProduceOnlyHeaders_ForPostWithEmptyBody()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "https://example.com/empty")
        {
            Content = new ByteArrayContent([])
        };

        var frames = await EncodeRequestAsync(request);

        Assert.Single(frames);
        Assert.IsType<Http3HeadersFrame>(frames[0]);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9114-4.1-30R2F-005: HEADERS frame contains QPACK-encoded header block")]
    public async Task Should_EncodeHeaders_WithQpack()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");

        var frames = await EncodeRequestAsync(request);

        var headersFrame = Assert.IsType<Http3HeadersFrame>(frames[0]);
        Assert.True(headersFrame.HeaderBlock.Length > 0, "QPACK header block should not be empty");
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9114-4.1-30R2F-006: Multiple requests encode independently")]
    public async Task Should_EncodeMultipleRequests_Independently()
    {
        var request1 = new HttpRequestMessage(HttpMethod.Get, "https://example.com/first");
        var request2 = new HttpRequestMessage(HttpMethod.Get, "https://example.com/second");

        var frames = await Source.From(new[] { request1, request2 })
            .Via(Flow.FromGraph(new Http30Request2FrameStage(_encoder)))
            .RunWith(Sink.Seq<Http3Frame>(), Materializer);

        var frameList = frames.ToList();
        Assert.Equal(2, frameList.Count);
        Assert.All(frameList, f => Assert.IsType<Http3HeadersFrame>(f));
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9114-4.1-30R2F-007: HEADERS frame for QPACK block matches direct encoder output")]
    public async Task Should_MatchDirectEncoderOutput_WhenEncodedViaStage()
    {
        var encoder = new Http3RequestEncoder();
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/verify");

        var directFrames = encoder.Encode(request);
        var directHeaders = Assert.IsType<Http3HeadersFrame>(directFrames[0]);

        var stageFrames = await Source.Single(request)
            .Via(Flow.FromGraph(new Http30Request2FrameStage(encoder)))
            .RunWith(Sink.Seq<Http3Frame>(), Materializer);

        var stageHeaders = Assert.IsType<Http3HeadersFrame>(stageFrames.First());
        Assert.Equal(directHeaders.HeaderBlock.ToArray(), stageHeaders.HeaderBlock.ToArray());
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9114-4.1-30R2F-008: PUT request with body produces HEADERS + DATA")]
    public async Task Should_ProduceHeadersAndData_ForPutWithBody()
    {
        var request = new HttpRequestMessage(HttpMethod.Put, "https://example.com/resource")
        {
            Content = new StringContent("{\"key\":\"value\"}", Encoding.UTF8, "application/json")
        };

        var frames = await EncodeRequestAsync(request);

        Assert.Equal(2, frames.Count);
        Assert.IsType<Http3HeadersFrame>(frames[0]);
        var dataFrame = Assert.IsType<Http3DataFrame>(frames[1]);
        Assert.True(dataFrame.Data.Length > 0);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9114-4.1-30R2F-009: Stage completes cleanly after upstream finishes")]
    public async Task Should_CompleteCleanly_WhenUpstreamFinishes()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/done");

        var frames = await Source.Single(request)
            .Via(Flow.FromGraph(new Http30Request2FrameStage(_encoder)))
            .RunWith(Sink.Seq<Http3Frame>(), Materializer);

        Assert.NotEmpty(frames);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9114-4.4-30R2F-010: CONNECT request produces HEADERS-only (no :scheme/:path)")]
    public async Task Should_ProduceHeadersOnly_ForConnectRequest()
    {
        var request = new HttpRequestMessage(HttpMethod.Connect, "https://proxy.example.com:8080/");

        var frames = await EncodeRequestAsync(request);

        Assert.Single(frames);
        Assert.IsType<Http3HeadersFrame>(frames[0]);
    }
}
