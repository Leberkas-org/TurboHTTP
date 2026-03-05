using Akka.Streams.Dsl;
using TurboHttp.Protocol;
using TurboHttp.Streams.Stages;

namespace TurboHttp.StreamTests.Http20;

public sealed class Request2FrameStageTests : StreamTestBase
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

    [Fact(Timeout = 10_000, DisplayName = "RFC-9113-§8.3.1: Emits HEADERS frame with :method pseudo-header")]
    public async Task ST_20_REQ_001_Headers_Frame_Contains_Method_PseudoHeader()
    {
        var encoder = new Http2RequestEncoder();
        var frames = await EncodeAsync(encoder, GetRequest());

        Assert.True(frames.Count >= 1);
        var headersFrame = Assert.IsType<HeadersFrame>(frames[0]);
        var headers = DecodeHpack(headersFrame.HeaderBlockFragment);
        Assert.Contains(headers, h => h.Name == ":method");
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC-9113-§8.3.1: Emits :path, :scheme, :authority pseudo-headers")]
    public async Task ST_20_REQ_002_Headers_Frame_Contains_All_Four_Pseudo_Headers()
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

    [Fact(Timeout = 10_000, DisplayName = "RFC-9113-§8.1: Stream IDs are odd and strictly ascending (1, 3, 5…)")]
    public async Task ST_20_REQ_003_StreamIds_Are_Odd_And_Ascending()
    {
        var encoder = new Http2RequestEncoder();
        var frames = await EncodeAsync(encoder, GetRequest(), GetRequest());

        // Each GET produces exactly one HEADERS frame
        var headersFrames = frames.OfType<HeadersFrame>().ToList();
        Assert.Equal(2, headersFrames.Count);

        Assert.Equal(1, headersFrames[0].StreamId);
        Assert.Equal(3, headersFrames[1].StreamId);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC-9113-§8.1: POST request emits HEADERS then DATA frame")]
    public async Task ST_20_REQ_004_Post_Request_Emits_Headers_Then_Data_Frame()
    {
        var encoder = new Http2RequestEncoder();
        var frames = await EncodeAsync(encoder, PostRequest());

        Assert.True(frames.Count >= 2, $"Expected at least 2 frames (HEADERS + DATA), got {frames.Count}");
        Assert.IsType<HeadersFrame>(frames[0]);
        Assert.IsType<DataFrame>(frames[1]);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC-9113-§8.3.1: GET request has END_STREAM flag set on HEADERS frame")]
    public async Task ST_20_REQ_005_Get_Request_Has_EndStream_On_Headers_Frame()
    {
        var encoder = new Http2RequestEncoder();
        var frames = await EncodeAsync(encoder, GetRequest());

        var headersFrame = Assert.IsType<HeadersFrame>(frames[0]);
        Assert.True(headersFrame.EndStream, "GET request HEADERS frame must have END_STREAM set");
    }
}