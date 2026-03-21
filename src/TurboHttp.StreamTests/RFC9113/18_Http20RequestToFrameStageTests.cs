using Akka.Streams.Dsl;
using TurboHttp.Protocol.RFC7541;
using TurboHttp.Protocol.RFC9113;
using TurboHttp.Streams.Stages.Encoding;

namespace TurboHttp.StreamTests.RFC9113;

/// <summary>
/// Tests the HTTP/2 request-to-frame conversion stage per RFC 9113.
/// Verifies that HttpRequestMessage objects are correctly converted to HEADERS and DATA frames with HPACK encoding.
/// </summary>
/// <remarks>
/// Stage under test: <see cref="Request2FrameStage"/>.
/// RFC 9113 §8.1: HTTP/2 request message mapping to HEADERS frames and pseudo-header fields.
/// </remarks>
public sealed class Http20RequestToFrameStageTests : StreamTestBase
{
    private static HttpRequestMessage GetRequest(string url = "http://example.com/path")
        => new(HttpMethod.Get, url);

    private static HttpRequestMessage PostRequest(string url = "http://example.com/path", string body = "hello")
    {
        return new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes(body))
        };
    }

    private async Task<IReadOnlyList<Http2Frame>> EncodeAsync(Http2RequestEncoder encoder,
        params HttpRequestMessage[] requests)
    {
        var streamId = 1;
        var source = Source.From<(HttpRequestMessage, int)>(requests.Select(x =>
        {
            var t = streamId;
            streamId += 2;
            return (x, t);
        }));
        return await source
            .Via(Flow.FromGraph(new Request2FrameStage(encoder)))
            .RunWith(Sink.Seq<Http2Frame>(), Materializer);
    }

    private static List<HpackHeader> DecodeHpack(ReadOnlyMemory<byte> headerBlock)
        => new HpackDecoder().Decode(headerBlock.Span);

    [Fact(Timeout = 10_000, DisplayName = "RFC9113-8.3.1-20RF-001: Emits HEADERS frame with :method pseudo-header")]
    public async Task Should_EmitHeadersFrameWithMethodPseudoHeader_When_RequestEncoded()
    {
        var encoder = new Http2RequestEncoder();
        var frames = await EncodeAsync(encoder, GetRequest());

        Assert.True(frames.Count >= 1);
        var headersFrame = Assert.IsType<HeadersFrame>(frames[0]);
        var headers = DecodeHpack(headersFrame.HeaderBlockFragment);
        Assert.Contains(headers, h => h.Name == ":method");
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9113-8.3.1-20RF-002: Emits :path, :scheme, :authority pseudo-headers")]
    public async Task Should_EmitHeadersFrameWithAllFourPseudoHeaders_When_RequestEncoded()
    {
        var encoder = new Http2RequestEncoder();
        var frames = await EncodeAsync(encoder, GetRequest("http://example.com/resource?q=1"));

        var headersFrame = Assert.IsType<HeadersFrame>(frames[0]);
        var headers = DecodeHpack(headersFrame.HeaderBlockFragment);
        var names = headers.Select(h => h.Name).ToList();

        Assert.Contains(":method", names);
        Assert.Contains(":path", names);
        Assert.Contains(":scheme", names);
        Assert.Contains(":authority", names);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9113-8.1-20RF-003: Stream IDs are odd and strictly ascending (1, 3, 5…)")]
    public async Task Should_AssignOddAscendingStreamIds_When_MultipleRequestsEncoded()
    {
        var encoder = new Http2RequestEncoder();
        var frames = await EncodeAsync(encoder, GetRequest(), GetRequest());

        // Each GET produces exactly one HEADERS frame
        var headersFrames = frames.OfType<HeadersFrame>().ToList();
        Assert.Equal(2, headersFrames.Count);

        Assert.Equal(1, headersFrames[0].StreamId);
        Assert.Equal(3, headersFrames[1].StreamId);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9113-8.1-20RF-004: POST request emits HEADERS then DATA frame")]
    public async Task Should_EmitHeadersThenDataFrame_When_EncodingPostRequest()
    {
        var encoder = new Http2RequestEncoder();
        var frames = await EncodeAsync(encoder, PostRequest());

        Assert.True(frames.Count >= 2, $"Expected at least 2 frames (HEADERS + DATA), got {frames.Count}");
        Assert.IsType<HeadersFrame>(frames[0]);
        Assert.IsType<DataFrame>(frames[1]);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9113-8.3.1-20RF-005: GET request has END_STREAM flag set on HEADERS frame")]
    public async Task Should_SetEndStreamOnHeadersFrame_When_EncodingGetRequest()
    {
        var encoder = new Http2RequestEncoder();
        var frames = await EncodeAsync(encoder, GetRequest());

        var headersFrame = Assert.IsType<HeadersFrame>(frames[0]);
        Assert.True(headersFrame.EndStream, "GET request HEADERS frame must have END_STREAM set");
    }
}