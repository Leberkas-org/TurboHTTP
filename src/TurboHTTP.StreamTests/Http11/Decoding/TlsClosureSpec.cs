using System.Net;
using Akka.Streams.Dsl;
using TurboHTTP.Internal;
using TurboHTTP.Streams.Stages.Decoding;
using TextEncoding = System.Text.Encoding;

namespace TurboHTTP.StreamTests.Http11.Decoding;

/// <summary>
/// Tests TLS closure alert detection per RFC 9112 §9.8.
/// Verifies that the HTTP/1.1 decoder stage correctly distinguishes clean TLS close
/// (close_notify received) from abrupt TCP disconnection, and handles
/// connection-close-delimited response bodies accordingly.
/// </summary>
/// <remarks>
/// Stage under test: <see cref="Http11DecoderStage"/>.
/// RFC 9112 §9.8: A server MAY close the connection at the end of a response when
/// the response does not include Content-Length or Transfer-Encoding.
/// </remarks>
public sealed class TlsClosureSpec : StreamTestBase
{
    private static NetworkBuffer Chunk(string ascii)
    {
        var bytes = TextEncoding.Latin1.GetBytes(ascii);
        return NetworkBuffer.FromArray(bytes);
    }

    private static IInputItem CloseSignal(TlsCloseKind kind)
    {
        return new CloseSignalItem(kind) { Key = RequestEndpoint.Default };
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9112-9.8")]
    public async Task TlsClosure_should_complete_response_when_clean_tls_closure()
    {
        // Server sends headers + body without Content-Length, then cleanly closes TLS connection.
        // The decoder should treat the buffered body as complete.
        var items = new IInputItem[]
        {
            Chunk("HTTP/1.1 200 OK\r\n\r\n"),
            Chunk("hello world"),
            CloseSignal(TlsCloseKind.CleanClose)
        };

        var source = Source.From(items);
        var response = await source
            .Via(Flow.FromGraph(new Http11DecoderStage()))
            .RunWith(Sink.First<HttpResponseMessage>(), Materializer);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal("hello world", body);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9112-9.8")]
    public async Task TlsClosure_should_mark_incomplete_when_abrupt_close()
    {
        // Server sends headers + partial body without Content-Length, then the TCP connection
        // drops abruptly (no close_notify). The decoder should NOT emit a response.
        var items = new IInputItem[]
        {
            Chunk("HTTP/1.1 200 OK\r\n\r\n"),
            Chunk("partial"),
            CloseSignal(TlsCloseKind.AbruptClose)
        };

        var source = Source.From(items);
        var responses = await source
            .Via(Flow.FromGraph(new Http11DecoderStage()))
            .RunWith(Sink.Seq<HttpResponseMessage>(), Materializer);

        // Abrupt close discards incomplete response — no messages should be emitted.
        Assert.Empty(responses);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9112-9.8")]
    public async Task TlsClosure_should_decode_normally_when_content_length_present_before_clean_close()
    {
        // When Content-Length is present, the response is already fully framed —
        // the clean close signal should not affect the already-emitted response.
        var items = new IInputItem[]
        {
            Chunk("HTTP/1.1 200 OK\r\nContent-Length: 5\r\n\r\nhello"),
            CloseSignal(TlsCloseKind.CleanClose)
        };

        var source = Source.From(items);
        var responses = await source
            .Via(Flow.FromGraph(new Http11DecoderStage()))
            .RunWith(Sink.Seq<HttpResponseMessage>(), Materializer);

        Assert.Single(responses);
        Assert.Equal(HttpStatusCode.OK, responses[0].StatusCode);
        var body = await responses[0].Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal("hello", body);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9112-9.8")]
    public async Task TlsClosure_should_complete_with_empty_body_when_clean_close_after_headers()
    {
        // Server sends only headers (no body data) then cleanly closes.
        // The response should have an empty body.
        var items = new IInputItem[]
        {
            Chunk("HTTP/1.1 200 OK\r\n\r\n"),
            CloseSignal(TlsCloseKind.CleanClose)
        };

        var source = Source.From(items);
        var response = await source
            .Via(Flow.FromGraph(new Http11DecoderStage()))
            .RunWith(Sink.First<HttpResponseMessage>(), Materializer);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal(string.Empty, body);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9112-9.8")]
    public async Task TlsClosure_should_reassemble_body_when_clean_close_after_multiple_chunks()
    {
        // Body arrives in multiple TCP segments before clean TLS close.
        var items = new IInputItem[]
        {
            Chunk("HTTP/1.1 200 OK\r\n\r\n"),
            Chunk("hello"),
            Chunk(" "),
            Chunk("world"),
            CloseSignal(TlsCloseKind.CleanClose)
        };

        var source = Source.From(items);
        var response = await source
            .Via(Flow.FromGraph(new Http11DecoderStage()))
            .RunWith(Sink.First<HttpResponseMessage>(), Materializer);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal("hello world", body);
    }
}
