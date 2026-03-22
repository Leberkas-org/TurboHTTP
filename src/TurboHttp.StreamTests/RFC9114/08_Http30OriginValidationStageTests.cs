using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHttp.Protocol.RFC9114;
using TurboHttp.Streams.Stages.Encoding;

namespace TurboHttp.StreamTests.RFC9114;

/// <summary>
/// Tests HTTP/3 origin validation integration in the request encoding stage per RFC 9114 §10.3.
/// Verifies that <see cref="Http3OriginValidator"/> is invoked inside <see cref="Http30Request2FrameStage"/>
/// before QPACK encoding, rejecting URIs that could enable intermediary encapsulation attacks.
/// </summary>
/// <remarks>
/// Stage under test: <see cref="Http30Request2FrameStage"/>.
/// RFC 9114 §10.3: Request URIs must not contain userinfo, must have a scheme and path,
/// and must not contain fragments.
/// </remarks>
public sealed class Http30OriginValidationStageTests : StreamTestBase
{
    private readonly Http3RequestEncoder _encoder = new();

    private async Task<List<Http3Frame>> EncodeRequestAsync(HttpRequestMessage request)
    {
        var frameSink = Sink.Seq<Http3Frame>();
        var encoderSink = Sink.Seq<ReadOnlyMemory<byte>>();

        var graph = RunnableGraph.FromGraph(
            GraphDsl.Create(frameSink, encoderSink,
                (m1, m2) => (m1, m2),
                (b, fSink, eSink) =>
                {
                    var source = b.Add(Source.Single(request));
                    var stage = b.Add(new Http30Request2FrameStage(_encoder));

                    b.From(source).To(stage.In);
                    b.From(stage.OutFrame).To(fSink);
                    b.From(stage.OutEncoder).To(eSink);

                    return ClosedShape.Instance;
                }));

        var (framesTask, _) = graph.Run(Materializer);
        var frames = await framesTask;

        return frames.ToList();
    }

    // ──────────────────────────────────────────────────────────────────────
    // Userinfo Rejection (RFC 9114 §10.3)
    // ──────────────────────────────────────────────────────────────────────

    [Fact(Timeout = 10_000, DisplayName = "RFC9114-10.3-OV-001: URI with userinfo causes stage failure")]
    public async Task Should_FailStage_When_UriContainsUserinfo()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://user:pass@example.com/path");

        var ex = await Assert.ThrowsAsync<Http3Exception>(() => EncodeRequestAsync(request));

        Assert.Contains("userinfo", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9114-10.3-OV-002: URI with username-only userinfo causes stage failure")]
    public async Task Should_FailStage_When_UriContainsUsernameOnlyUserinfo()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://admin@example.com/path");

        var ex = await Assert.ThrowsAsync<Http3Exception>(() => EncodeRequestAsync(request));

        Assert.Contains("userinfo", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ──────────────────────────────────────────────────────────────────────
    // Valid URIs Pass Through
    // ──────────────────────────────────────────────────────────────────────

    [Fact(Timeout = 10_000, DisplayName = "RFC9114-10.3-OV-003: Valid HTTPS URI passes origin validation")]
    public async Task Should_ProduceFrames_When_UriIsValid()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/path");

        var frames = await EncodeRequestAsync(request);

        Assert.NotEmpty(frames);
        Assert.IsType<Http3HeadersFrame>(frames[0]);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9114-10.3-OV-004: CONNECT request passes origin validation")]
    public async Task Should_ProduceFrames_When_ConnectRequest()
    {
        var request = new HttpRequestMessage(HttpMethod.Connect, "https://proxy.example.com:8080/");

        var frames = await EncodeRequestAsync(request);

        Assert.NotEmpty(frames);
        Assert.IsType<Http3HeadersFrame>(frames[0]);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9114-10.3-OV-005: POST with valid URI passes origin validation")]
    public async Task Should_ProduceFrames_When_PostWithValidUri()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "https://api.example.com/submit")
        {
            Content = new StringContent("body")
        };

        var frames = await EncodeRequestAsync(request);

        Assert.True(frames.Count >= 1);
        Assert.IsType<Http3HeadersFrame>(frames[0]);
    }
}
