using System.Net;
using Akka.Streams.Dsl;
using TurboHTTP.Internal;
using TurboHTTP.Protocol.Semantics;
using TurboHTTP.Streams.Stages.Decoding;
using TextEncoding = System.Text.Encoding;

namespace TurboHTTP.StreamTests.Http11.Decoding;

/// <summary>
/// Tests incomplete close recovery per RFC 9112 §8 + §9.8.
/// Verifies that the HTTP/1.1 decoder stage correctly identifies incomplete responses
/// (chunked without zero-chunk, truncated Content-Length) and that the retry evaluator
/// recommends retrying idempotent requests after incomplete close.
/// </summary>
/// <remarks>
/// Stage under test: <see cref="Http11DecoderStage"/>.
/// RFC 9112 §7.1: Chunked encoding MUST be terminated by a zero-length chunk.
/// RFC 9112 §6.2: Content-Length specifies the exact number of octets in the body.
/// RFC 9112 §9.8: Connection close is a valid body delimiter only when no CL/TE present.
/// RFC 9110 §9.2.2: Idempotent methods may be retried after network failure.
/// </remarks>
public sealed class IncompleteCloseRecoverySpec : StreamTestBase
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
    [Trait("RFC", "RFC9112-8")]
    public async Task IncompleteCloseRecovery_should_be_incomplete_when_no_zero_chunk()
    {
        // Server sends a chunked response with one chunk but no terminating zero-chunk,
        // then cleanly closes the connection. The decoder must NOT emit the response
        // because the chunked encoding is incomplete per RFC 9112 §7.1.
        var items = new IInputItem[]
        {
            Chunk("HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\n\r\n"),
            Chunk("5\r\nhello\r\n"),
            CloseSignal(TlsCloseKind.CleanClose)
        };

        var source = Source.From(items);
        var responses = await source
            .Via(Flow.FromGraph(new Http11DecoderStage()))
            .RunWith(Sink.Seq<HttpResponseMessage>(), Materializer);

        // Incomplete chunked response — no zero-chunk terminator → no emission.
        Assert.Empty(responses);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9112-8")]
    public async Task IncompleteCloseRecovery_should_be_incomplete_when_body_truncated()
    {
        // Server declares Content-Length: 20 but only sends 5 bytes of body,
        // then cleanly closes the connection. The decoder must NOT emit the response
        // because the body is truncated per RFC 9112 §6.2.
        var items = new IInputItem[]
        {
            Chunk("HTTP/1.1 200 OK\r\nContent-Length: 20\r\n\r\nhello"),
            CloseSignal(TlsCloseKind.CleanClose)
        };

        var source = Source.From(items);
        var responses = await source
            .Via(Flow.FromGraph(new Http11DecoderStage()))
            .RunWith(Sink.Seq<HttpResponseMessage>(), Materializer);

        // Content-Length mismatch — body truncated → no emission.
        Assert.Empty(responses);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9112-8")]
    public async Task IncompleteCloseRecovery_should_complete_when_no_cl_and_clean_close()
    {
        // Server sends a response with no Content-Length and no Transfer-Encoding,
        // then cleanly closes the connection. Per RFC 9112 §9.8, this is a valid
        // connection-close-delimited response. The body is complete.
        var items = new IInputItem[]
        {
            Chunk("HTTP/1.1 200 OK\r\n\r\n"),
            Chunk("complete body"),
            CloseSignal(TlsCloseKind.CleanClose)
        };

        var source = Source.From(items);
        var response = await source
            .Via(Flow.FromGraph(new Http11DecoderStage()))
            .RunWith(Sink.First<HttpResponseMessage>(), Materializer);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal("complete body", body);
    }

    [Fact]
    [Trait("RFC", "RFC9110-9.2.2")]
    public void IncompleteCloseRecovery_should_retry_when_idempotent_and_incomplete()
    {
        // An incomplete close is a network-level failure. Per RFC 9110 §9.2.2,
        // idempotent methods (GET, HEAD, PUT, DELETE) may be safely retried.
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/resource");

        var decision = RetryEvaluator.Evaluate(
            request,
            response: null,
            networkFailure: true,
            bodyPartiallyConsumed: false,
            attemptCount: 1);

        Assert.True(decision.ShouldRetry);
    }

    [Fact]
    [Trait("RFC", "RFC9110-9.2.2")]
    public void IncompleteCloseRecovery_should_not_retry_when_non_idempotent_and_incomplete()
    {
        // POST is not idempotent — incomplete close must not trigger automatic retry.
        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/resource");

        var decision = RetryEvaluator.Evaluate(
            request,
            response: null,
            networkFailure: true,
            bodyPartiallyConsumed: false,
            attemptCount: 1);

        Assert.False(decision.ShouldRetry);
    }
}
