using Akka.Streams.Dsl;
using TurboHttp.Internal;
using TurboHttp.Streams.Stages.Decoding;
using TextEncoding = System.Text.Encoding;

namespace TurboHttp.StreamTests.Http11.Chunking;

/// <summary>
/// RFC-tagged tests for chunked transfer encoding in the HTTP/1.1 decoder stage per RFC 9112.
/// Verifies that chunked body reassembly, chunk extensions, and trailing headers are correctly handled.
/// </summary>
/// <remarks>
/// Stage under test: <see cref="Http11DecoderStage"/>.
/// RFC 9112 §7.1: HTTP/1.1 chunked transfer encoding format and body reassembly.
/// </remarks>
public sealed class Http11ChunkedTransferSpec : StreamTestBase
{
    private static IInputItem Chunk(string ascii)
    {
        var bytes = TextEncoding.Latin1.GetBytes(ascii);
        return NetworkBuffer.FromArray(bytes);
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

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9112-7.1")]
    public async Task Http11ChunkedTransfer_should_decode_body_when_single_chunk()
    {
        var response = await DecodeAsync(
            "HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\n\r\n5\r\nhello\r\n0\r\n\r\n");

        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal("hello", body);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9112-7.1")]
    public async Task Http11ChunkedTransfer_should_concatenate_chunks_when_multiple_chunks_present()
    {
        var response = await DecodeAsync(
            "HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\n\r\n" +
            "5\r\nhello\r\n" +
            "1\r\n \r\n" +
            "5\r\nworld\r\n" +
            "0\r\n\r\n");

        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal("hello world", body);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9112-7.1")]
    public async Task Http11ChunkedTransfer_should_concatenate_three_equal_chunks_when_chunked_encoding()
    {
        var response = await DecodeAsync(
            "HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\n\r\n" +
            "3\r\nfoo\r\n3\r\nbar\r\n3\r\nbaz\r\n0\r\n\r\n");

        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal("foobarbaz", body);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9112-7.1")]
    public async Task Http11ChunkedTransfer_should_end_stream_when_zero_length_terminator()
    {
        // Use Take(1) + Sink.Seq instead of bare Sink.Seq to avoid waiting
        // for upstream completion — the decoder stage keeps the stream open
        // for potential pipelined responses.
        var responses = await Source.From(new[]
            {
                Chunk("HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\n\r\n" +
                      "5\r\nhello\r\n0\r\n\r\n")
            })
            .Via(Flow.FromGraph(new Http11DecoderStage()))
            .Take(1)
            .RunWith(Sink.Seq<HttpResponseMessage>(), Materializer);

        Assert.Single(responses);
        var body = await responses[0].Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal("hello", body);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9112-7.1")]
    public async Task Http11ChunkedTransfer_should_produce_empty_body_when_only_terminator_chunk()
    {
        var response = await DecodeAsync(
            "HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\n\r\n0\r\n\r\n");

        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal("", body);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9112-7.1")]
    public async Task Http11ChunkedTransfer_should_ignore_chunk_extension_when_present()
    {
        var response = await DecodeAsync(
            "HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\n\r\n" +
            "5;ext=val\r\nhello\r\n0\r\n\r\n");

        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal("hello", body);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9112-7.1")]
    public async Task Http11ChunkedTransfer_should_ignore_name_only_chunk_extension_when_present()
    {
        var response = await DecodeAsync(
            "HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\n\r\n" +
            "5;myext\r\nhello\r\n0\r\n\r\n");

        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal("hello", body);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9112-7.1")]
    public async Task Http11ChunkedTransfer_should_ignore_chunk_extension_on_terminator_when_present()
    {
        var response = await DecodeAsync(
            "HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\n\r\n" +
            "5\r\nhello\r\n0;end=true\r\n\r\n");

        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal("hello", body);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9112-7.1")]
    public async Task Http11ChunkedTransfer_should_parse_trailer_headers_when_after_last_chunk()
    {
        var response = await DecodeAsync(
            "HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\n\r\n" +
            "5\r\nhello\r\n0\r\nX-Checksum: abc123\r\n\r\n");

        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal("hello", body);
        Assert.True(response.TrailingHeaders.TryGetValues("X-Checksum", out var values));
        Assert.Equal("abc123", values.Single());
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9112-7.1")]
    public async Task Http11ChunkedTransfer_should_parse_multiple_trailer_headers_when_after_last_chunk()
    {
        var response = await DecodeAsync(
            "HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\n\r\n" +
            "5\r\nhello\r\n0\r\nX-Checksum: abc123\r\nX-Signature: sig456\r\n\r\n");

        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal("hello", body);

        Assert.True(response.TrailingHeaders.TryGetValues("X-Checksum", out var checksumValues));
        Assert.Equal("abc123", checksumValues.Single());

        Assert.True(response.TrailingHeaders.TryGetValues("X-Signature", out var sigValues));
        Assert.Equal("sig456", sigValues.Single());
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9112-7.1")]
    public async Task Http11ChunkedTransfer_should_have_empty_trailing_headers_when_no_trailers()
    {
        var response = await DecodeAsync(
            "HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\n\r\n" +
            "5\r\nhello\r\n0\r\n\r\n");

        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal("hello", body);
        Assert.Empty(response.TrailingHeaders);
    }
}
