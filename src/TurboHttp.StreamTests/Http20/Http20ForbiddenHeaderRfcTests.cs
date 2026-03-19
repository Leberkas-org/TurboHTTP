using Akka.Streams.Dsl;
using TurboHttp.Protocol.RFC7541;
using TurboHttp.Protocol.RFC9113;
using TurboHttp.Streams.Stages;

namespace TurboHttp.StreamTests.Http20;

public sealed class Http20ForbiddenHeaderRfcTests : StreamTestBase
{
    /// <summary>
    /// Runs requests through StreamIdAllocatorStage → Request2FrameStage and collects all frames.
    /// </summary>
    private async Task<IReadOnlyList<Http2Frame>> RunAsync(params HttpRequestMessage[] requests)
    {
        var encoder = new Http2RequestEncoder();

        return await Source.From(requests)
            .Via(Flow.FromGraph(new StreamIdAllocatorStage()))
            .Via(Flow.FromGraph(new Request2FrameStage(encoder)))
            .RunWith(Sink.Seq<Http2Frame>(), Materializer);
    }

    /// <summary>
    /// Decodes the HPACK header block from a HEADERS frame into a list of header fields.
    /// </summary>
    private static List<HpackHeader> DecodeHeaders(HeadersFrame frame)
        => new HpackDecoder().Decode(frame.HeaderBlockFragment.Span);

    private static HttpRequestMessage RequestWithHeader(string headerName, string headerValue)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        request.Headers.TryAddWithoutValidation(headerName, headerValue);
        return request;
    }

    // ─── H2FH-001: connection header stripped ─────────────────────────────────────

    [Fact(Timeout = 10_000, DisplayName = "RFC9113-8.2.2-H2FH-001: connection header not present in wire format")]
    public async Task H2FH_001_Connection_Header_Stripped()
    {
        var frames = await RunAsync(RequestWithHeader("connection", "keep-alive"));

        var headers = DecodeHeaders(Assert.IsType<HeadersFrame>(frames[0]));
        Assert.DoesNotContain(headers, h => h.Name == "connection");
    }

    // ─── H2FH-002: transfer-encoding header stripped ──────────────────────────────

    [Fact(Timeout = 10_000, DisplayName = "RFC9113-8.2.2-H2FH-002: transfer-encoding header not present in wire format")]
    public async Task H2FH_002_Transfer_Encoding_Header_Stripped()
    {
        var frames = await RunAsync(RequestWithHeader("transfer-encoding", "chunked"));

        var headers = DecodeHeaders(Assert.IsType<HeadersFrame>(frames[0]));
        Assert.DoesNotContain(headers, h => h.Name == "transfer-encoding");
    }

    // ─── H2FH-003: upgrade header stripped ────────────────────────────────────────

    [Fact(Timeout = 10_000, DisplayName = "RFC9113-8.2.2-H2FH-003: upgrade header not present in wire format")]
    public async Task H2FH_003_Upgrade_Header_Stripped()
    {
        var frames = await RunAsync(RequestWithHeader("upgrade", "h2c"));

        var headers = DecodeHeaders(Assert.IsType<HeadersFrame>(frames[0]));
        Assert.DoesNotContain(headers, h => h.Name == "upgrade");
    }

    // ─── H2FH-004: keep-alive header stripped ─────────────────────────────────────

    [Fact(Timeout = 10_000, DisplayName = "RFC9113-8.2.2-H2FH-004: keep-alive header not present in wire format")]
    public async Task H2FH_004_Keep_Alive_Header_Stripped()
    {
        var frames = await RunAsync(RequestWithHeader("keep-alive", "timeout=5"));

        var headers = DecodeHeaders(Assert.IsType<HeadersFrame>(frames[0]));
        Assert.DoesNotContain(headers, h => h.Name == "keep-alive");
    }

    // ─── H2FH-005: custom header preserved ────────────────────────────────────────

    [Fact(Timeout = 10_000, DisplayName = "RFC9113-8.2.2-H2FH-005: custom header (x-custom) present in wire format")]
    public async Task H2FH_005_Custom_Header_Preserved()
    {
        var frames = await RunAsync(RequestWithHeader("x-custom", "my-value"));

        var headers = DecodeHeaders(Assert.IsType<HeadersFrame>(frames[0]));
        var custom = Assert.Single(headers, h => h.Name == "x-custom");
        Assert.Equal("my-value", custom.Value);
    }
}
