using System.Text;
using Akka.Streams.Dsl;
using TurboHttp.IO.Stages;
using TurboHttp.Streams.Stages;

namespace TurboHttp.StreamTests.Http11;

/// <summary>
/// RFC 9112 §7.1 — Chunked Transfer Coding compliance tests for Http11DecoderStage.
/// </summary>
public sealed class Http11DecoderStageChunkedRfcTests : StreamTestBase
{
    private static IInputItem Chunk(string ascii)
    {
        var bytes = Encoding.Latin1.GetBytes(ascii);
        return new DataItem(new SimpleMemoryOwner(bytes), bytes.Length) { Key = RequestEndpoint.Default };
    }

    private async Task<HttpResponseMessage> DecodeAsync(params string[] chunks)
    {
        var source = Source.From(chunks.Select(Chunk));
        return await source
            .Via(Flow.FromGraph(new Http11DecoderStage()))
            .RunWith(Sink.First<HttpResponseMessage>(), Materializer);
    }

    private async Task<IReadOnlyList<HttpResponseMessage>> DecodeAllAsync(params string[] chunks)
    {
        var source = Source.From(chunks.Select(Chunk));
        return await source
            .Via(Flow.FromGraph(new Http11DecoderStage()))
            .RunWith(Sink.Seq<HttpResponseMessage>(), Materializer);
    }

    // ── 11D-CH-001: Single chunk → body = "hello" ──────────────────────────────

    [Fact(Timeout = 10_000, DisplayName = "RFC9112-7.1-11CH-001: Single chunk 5\\r\\nhello\\r\\n0\\r\\n\\r\\n → body = hello")]
    public async Task _11D_CH_001_SingleChunk_BodyDecoded()
    {
        var response = await DecodeAsync(
            "HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\n\r\n5\r\nhello\r\n0\r\n\r\n");

        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("hello", body);
    }

    // ── 11D-CH-002: Multiple chunks → bodies correctly concatenated ─────────────

    [Fact(Timeout = 10_000, DisplayName = "RFC9112-7.1-11CH-002: Multiple chunks concatenated into single body")]
    public async Task _11D_CH_002_MultipleChunks_Concatenated()
    {
        var response = await DecodeAsync(
            "HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\n\r\n" +
            "5\r\nhello\r\n" +
            "1\r\n \r\n" +
            "5\r\nworld\r\n" +
            "0\r\n\r\n");

        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("hello world", body);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9112-7.1-11CH-002b: Three equal-sized chunks concatenated")]
    public async Task _11D_CH_002b_ThreeEqualChunks_Concatenated()
    {
        var response = await DecodeAsync(
            "HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\n\r\n" +
            "3\r\nfoo\r\n3\r\nbar\r\n3\r\nbaz\r\n0\r\n\r\n");

        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("foobarbaz", body);
    }

    // ── 11D-CH-003: Zero-length terminator → stream ends ────────────────────────

    [Fact(Timeout = 10_000, DisplayName = "RFC9112-7.1-11CH-003: Zero-length terminator 0\\r\\n\\r\\n ends stream")]
    public async Task _11D_CH_003_ZeroLengthTerminator_StreamEnds()
    {
        var responses = await DecodeAllAsync(
            "HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\n\r\n" +
            "5\r\nhello\r\n0\r\n\r\n");

        Assert.Single(responses);
        var body = await responses[0].Content.ReadAsStringAsync();
        Assert.Equal("hello", body);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9112-7.1-11CH-003b: Empty chunked body (only terminator) → empty body")]
    public async Task _11D_CH_003b_EmptyChunkedBody_OnlyTerminator()
    {
        var response = await DecodeAsync(
            "HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\n\r\n0\r\n\r\n");

        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("", body);
    }

    // ── 11D-CH-004: Chunk extension (;ext=val) is ignored ───────────────────────

    [Fact(Timeout = 10_000, DisplayName = "RFC9112-7.1-11CH-004: Chunk extension ;ext=val is ignored, body intact")]
    public async Task _11D_CH_004_ChunkExtension_Ignored()
    {
        var response = await DecodeAsync(
            "HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\n\r\n" +
            "5;ext=val\r\nhello\r\n0\r\n\r\n");

        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("hello", body);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9112-7.1-11CH-004b: Name-only chunk extension is ignored")]
    public async Task _11D_CH_004b_NameOnlyChunkExtension_Ignored()
    {
        var response = await DecodeAsync(
            "HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\n\r\n" +
            "5;myext\r\nhello\r\n0\r\n\r\n");

        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("hello", body);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9112-7.1-11CH-004c: Chunk extension on terminator chunk is ignored")]
    public async Task _11D_CH_004c_ChunkExtensionOnTerminator_Ignored()
    {
        var response = await DecodeAsync(
            "HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\n\r\n" +
            "5\r\nhello\r\n0;end=true\r\n\r\n");

        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("hello", body);
    }

    // ── 11D-CH-005: Trailers after last chunk → correctly parsed or ignored ─────

    [Fact(Timeout = 10_000, DisplayName = "RFC9112-7.1-11CH-005: Trailer header after last chunk is parsed")]
    public async Task _11D_CH_005_TrailerHeaders_Parsed()
    {
        var response = await DecodeAsync(
            "HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\n\r\n" +
            "5\r\nhello\r\n0\r\nX-Checksum: abc123\r\n\r\n");

        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("hello", body);
        Assert.True(response.TrailingHeaders.TryGetValues("X-Checksum", out var values));
        Assert.Equal("abc123", values.Single());
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9112-7.1-11CH-005b: Multiple trailer headers after last chunk")]
    public async Task _11D_CH_005b_MultipleTrailerHeaders_Parsed()
    {
        var response = await DecodeAsync(
            "HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\n\r\n" +
            "5\r\nhello\r\n0\r\nX-Checksum: abc123\r\nX-Signature: sig456\r\n\r\n");

        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("hello", body);

        Assert.True(response.TrailingHeaders.TryGetValues("X-Checksum", out var checksumValues));
        Assert.Equal("abc123", checksumValues.Single());

        Assert.True(response.TrailingHeaders.TryGetValues("X-Signature", out var sigValues));
        Assert.Equal("sig456", sigValues.Single());
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9112-7.1-11CH-005c: No trailers — empty trailer section after terminator")]
    public async Task _11D_CH_005c_NoTrailers_EmptyTrailerSection()
    {
        var response = await DecodeAsync(
            "HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\n\r\n" +
            "5\r\nhello\r\n0\r\n\r\n");

        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("hello", body);
        Assert.Empty(response.TrailingHeaders);
    }
}