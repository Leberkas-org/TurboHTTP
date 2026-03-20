using System.Text;
using Akka.Streams.Dsl;
using TurboHttp.Internal;
using TurboHttp.Streams.Stages;

namespace TurboHttp.StreamTests.RFC9112;

/// <summary>
/// RFC-tagged tests for chunked transfer encoding in the HTTP/1.1 decoder stage per RFC 9112.
/// Verifies that chunked body reassembly, chunk extensions, and trailing headers are correctly handled.
/// </summary>
/// <remarks>
/// Stage under test: <see cref="Http11DecoderStage"/>.
/// RFC 9112 §7.1: HTTP/1.1 chunked transfer encoding format and body reassembly.
/// </remarks>
public sealed class Http11DecoderStageChunkedTransferTests : StreamTestBase
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

    [Fact(Timeout = 10_000, DisplayName = "RFC9112-7.1-11CH-001: Single chunk 5\\r\\nhello\\r\\n0\\r\\n\\r\\n → body = hello")]
    public async Task Should_DecodeBody_WhenSingleChunk()
    {
        var response = await DecodeAsync(
            "HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\n\r\n5\r\nhello\r\n0\r\n\r\n");

        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("hello", body);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9112-7.1-11CH-002: Multiple chunks concatenated into single body")]
    public async Task Should_ConcatenateChunks_WhenMultipleChunksPresent()
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
    public async Task Should_ConcatenateThreeEqualChunks_WhenChunkedEncoding()
    {
        var response = await DecodeAsync(
            "HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\n\r\n" +
            "3\r\nfoo\r\n3\r\nbar\r\n3\r\nbaz\r\n0\r\n\r\n");

        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("foobarbaz", body);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9112-7.1-11CH-003: Zero-length terminator 0\\r\\n\\r\\n ends stream")]
    public async Task Should_EndStream_WhenZeroLengthTerminator()
    {
        var responses = await DecodeAllAsync(
            "HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\n\r\n" +
            "5\r\nhello\r\n0\r\n\r\n");

        Assert.Single(responses);
        var body = await responses[0].Content.ReadAsStringAsync();
        Assert.Equal("hello", body);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9112-7.1-11CH-003b: Empty chunked body (only terminator) → empty body")]
    public async Task Should_ProduceEmptyBody_WhenOnlyTerminatorChunk()
    {
        var response = await DecodeAsync(
            "HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\n\r\n0\r\n\r\n");

        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("", body);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9112-7.1-11CH-004: Chunk extension ;ext=val is ignored, body intact")]
    public async Task Should_IgnoreChunkExtension_WhenPresent()
    {
        var response = await DecodeAsync(
            "HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\n\r\n" +
            "5;ext=val\r\nhello\r\n0\r\n\r\n");

        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("hello", body);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9112-7.1-11CH-004b: Name-only chunk extension is ignored")]
    public async Task Should_IgnoreNameOnlyChunkExtension_WhenPresent()
    {
        var response = await DecodeAsync(
            "HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\n\r\n" +
            "5;myext\r\nhello\r\n0\r\n\r\n");

        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("hello", body);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9112-7.1-11CH-004c: Chunk extension on terminator chunk is ignored")]
    public async Task Should_IgnoreChunkExtensionOnTerminator_WhenPresent()
    {
        var response = await DecodeAsync(
            "HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\n\r\n" +
            "5\r\nhello\r\n0;end=true\r\n\r\n");

        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("hello", body);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9112-7.1-11CH-005: Trailer header after last chunk is parsed")]
    public async Task Should_ParseTrailerHeaders_WhenAfterLastChunk()
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
    public async Task Should_ParseMultipleTrailerHeaders_WhenAfterLastChunk()
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
    public async Task Should_HaveEmptyTrailingHeaders_WhenNoTrailers()
    {
        var response = await DecodeAsync(
            "HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\n\r\n" +
            "5\r\nhello\r\n0\r\n\r\n");

        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("hello", body);
        Assert.Empty(response.TrailingHeaders);
    }
}