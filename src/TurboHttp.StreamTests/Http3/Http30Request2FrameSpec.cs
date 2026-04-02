using System.Text;
using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHttp.Protocol.Http3;
using TurboHttp.Streams.Stages.Encoding;

namespace TurboHttp.StreamTests.Http3;

/// <summary>
/// Tests the HTTP/3 request-to-frame conversion stage per RFC 9114 §4.1.
/// Verifies that <see cref="Http30Request2FrameStage"/> correctly produces
/// HEADERS frames (QPACK-encoded) and DATA frames for request bodies.
/// </summary>
/// <remarks>
/// Stage under test: <see cref="Http30Request2FrameStage"/>.
/// Unlike HTTP/2, HTTP/3 frames carry no stream identifier (QUIC handles that)
/// and header blocks are never split across CONTINUATION frames.
/// The stage has a FanOut shape: frames on <c>OutFrame</c>, encoder instructions on <c>OutEncoder</c>.
/// </remarks>
public sealed class Http30Request2FrameSpec : StreamTestBase
{
    private readonly Http3RequestEncoder _encoder = new();

    private async Task<(List<Http3Frame> Frames, List<ReadOnlyMemory<byte>> Instructions)> RunStageAsync(
        params HttpRequestMessage[] requests)
    {
        var frameSink = Sink.Seq<Http3Frame>();
        var encoderSink = Sink.Seq<ReadOnlyMemory<byte>>();

        var graph = RunnableGraph.FromGraph(
            GraphDsl.Create(frameSink, encoderSink,
                (m1, m2) => (m1, m2),
                (b, fSink, eSink) =>
                {
                    var source = b.Add(Source.From(requests));
                    var stage = b.Add(new Http30Request2FrameStage(_encoder));

                    b.From(source).To(stage.In);
                    b.From(stage.OutFrame).To(fSink);
                    b.From(stage.OutEncoder).To(eSink);

                    return ClosedShape.Instance;
                }));

        var (framesTask, instructionsTask) = graph.Run(Materializer);
        var frames = await framesTask;
        var instructions = await instructionsTask;

        return (frames.ToList(), instructions.ToList());
    }

    private async Task<List<Http3Frame>> EncodeRequestAsync(HttpRequestMessage request)
    {
        var (frames, _) = await RunStageAsync(request);
        return frames;
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9114-4.1")]
    public async Task Http30Request2Frame_should_produce_headers_frame_for_get_request()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/path");

        var frames = await EncodeRequestAsync(request);

        Assert.Single(frames);
        Assert.IsType<Http3HeadersFrame>(frames[0]);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9114-4.1")]
    public async Task Http30Request2Frame_should_produce_headers_and_data_frames_for_post_with_body()
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

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9114-4.1")]
    public async Task Http30Request2Frame_should_carry_body_bytes_in_data_frame()
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

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9114-4.1")]
    public async Task Http30Request2Frame_should_produce_only_headers_for_post_with_empty_body()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "https://example.com/empty")
        {
            Content = new ByteArrayContent([])
        };

        var frames = await EncodeRequestAsync(request);

        Assert.Single(frames);
        Assert.IsType<Http3HeadersFrame>(frames[0]);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9114-4.1")]
    public async Task Http30Request2Frame_should_encode_headers_with_qpack()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");

        var frames = await EncodeRequestAsync(request);

        var headersFrame = Assert.IsType<Http3HeadersFrame>(frames[0]);
        Assert.True(headersFrame.HeaderBlock.Length > 0, "QPACK header block should not be empty");
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9114-4.1")]
    public async Task Http30Request2Frame_should_encode_multiple_requests_independently()
    {
        var request1 = new HttpRequestMessage(HttpMethod.Get, "https://example.com/first");
        var request2 = new HttpRequestMessage(HttpMethod.Get, "https://example.com/second");

        var (frames, _) = await RunStageAsync(request1, request2);

        Assert.Equal(2, frames.Count);
        Assert.All(frames, f => Assert.IsType<Http3HeadersFrame>(f));
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9114-4.1")]
    public async Task Http30Request2Frame_should_match_direct_encoder_output_when_encoded_via_stage()
    {
        var encoder = new Http3RequestEncoder();
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/verify");

        var directFrames = encoder.Encode(request);
        var directHeaders = Assert.IsType<Http3HeadersFrame>(directFrames[0]);

        var frameSink = Sink.Seq<Http3Frame>();
        var encoderSink = Sink.Seq<ReadOnlyMemory<byte>>();

        var graph = RunnableGraph.FromGraph(
            GraphDsl.Create(frameSink, encoderSink,
                (m1, m2) => (m1, m2),
                (b, fSink, eSink) =>
                {
                    var source = b.Add(Source.Single(request));
                    var stage = b.Add(new Http30Request2FrameStage(encoder));

                    b.From(source).To(stage.In);
                    b.From(stage.OutFrame).To(fSink);
                    b.From(stage.OutEncoder).To(eSink);

                    return ClosedShape.Instance;
                }));

        var (framesTask, _) = graph.Run(Materializer);
        var stageFrames = await framesTask;

        var stageHeaders = Assert.IsType<Http3HeadersFrame>(stageFrames.First());
        Assert.Equal(directHeaders.HeaderBlock.ToArray(), stageHeaders.HeaderBlock.ToArray());
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9114-4.1")]
    public async Task Http30Request2Frame_should_produce_headers_and_data_for_put_with_body()
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

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9114-4.1")]
    public async Task Http30Request2Frame_should_complete_cleanly_when_upstream_finishes()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/done");

        var frames = await EncodeRequestAsync(request);

        Assert.NotEmpty(frames);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9114-4.4")]
    public async Task Http30Request2Frame_should_produce_headers_only_for_connect_request()
    {
        var request = new HttpRequestMessage(HttpMethod.Connect, "https://proxy.example.com:8080/");

        var frames = await EncodeRequestAsync(request);

        Assert.Single(frames);
        Assert.IsType<Http3HeadersFrame>(frames[0]);
    }
}
