using System.Text;
using TurboHTTP.Protocol.Syntax;
using TurboHTTP.Protocol.Syntax.Http11.Options;
using TurboHTTP.Protocol.Syntax.Http11.Server;

namespace TurboHTTP.Tests.Protocol.Syntax.Http11.Server;

public sealed class Http11ServerDecoderSecuritySpec
{
    private static Http11ServerDecoder MakeDecoder(SharedHttpOptions? shared = null)
    {
        var options = shared != null
            ? new Http11ServerDecoderOptions { Shared = shared }
            : Http11ServerDecoderOptions.Default;
        return new Http11ServerDecoder(options);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6.3")]
    public void Feed_should_reject_request_with_both_content_length_and_transfer_encoding()
    {
        var decoder = MakeDecoder();
        var request = "POST / HTTP/1.1\r\n" +
                      "Host: example.com\r\n" +
                      "Content-Length: 5\r\n" +
                      "Transfer-Encoding: chunked\r\n\r\n";
        var bytes = Encoding.ASCII.GetBytes(request);

        _ = Assert.Throws<HttpProtocolException>(() => decoder.Feed(bytes, out _));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6.1")]
    public void Feed_should_reject_transfer_encoding_in_http10_request()
    {
        var decoder = MakeDecoder();
        var request = "POST / HTTP/1.0\r\n" +
                      "Host: example.com\r\n" +
                      "Transfer-Encoding: chunked\r\n\r\n";
        var bytes = Encoding.ASCII.GetBytes(request);

        _ = Assert.Throws<HttpProtocolException>(() => decoder.Feed(bytes, out _));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-8.6")]
    public void Feed_should_reject_conflicting_content_length_values()
    {
        var decoder = MakeDecoder();
        var request = "POST / HTTP/1.1\r\n" +
                      "Host: example.com\r\n" +
                      "Content-Length: 5\r\n" +
                      "Content-Length: 10\r\n\r\n";
        var bytes = Encoding.ASCII.GetBytes(request);

        _ = Assert.Throws<HttpProtocolException>(() => decoder.Feed(bytes, out _));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-8.6")]
    public void Feed_should_reject_non_numeric_content_length()
    {
        var decoder = MakeDecoder();
        var request = "POST / HTTP/1.1\r\n" +
                      "Host: example.com\r\n" +
                      "Content-Length: abc\r\n\r\n";
        var bytes = Encoding.ASCII.GetBytes(request);

        _ = Assert.Throws<HttpProtocolException>(() => decoder.Feed(bytes, out _));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-8.6")]
    public void Feed_should_reject_negative_content_length()
    {
        var decoder = MakeDecoder();
        var request = "POST / HTTP/1.1\r\n" +
                      "Host: example.com\r\n" +
                      "Content-Length: -1\r\n\r\n";
        var bytes = Encoding.ASCII.GetBytes(request);

        _ = Assert.Throws<HttpProtocolException>(() => decoder.Feed(bytes, out _));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-8.6")]
    public void Feed_should_accept_duplicate_content_length_with_same_value()
    {
        var decoder = MakeDecoder();
        var request = "POST / HTTP/1.1\r\n" +
                      "Host: example.com\r\n" +
                      "Content-Length: 5\r\n" +
                      "Content-Length: 5\r\n\r\n" +
                      "hello";
        var bytes = Encoding.ASCII.GetBytes(request);

        var outcome = decoder.Feed(bytes, out _);

        Assert.Equal(DecodeOutcome.Complete, outcome);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-7.1")]
    public void Feed_should_parse_chunked_request_body()
    {
        var decoder = MakeDecoder();
        var request = "POST / HTTP/1.1\r\n" +
                      "Host: example.com\r\n" +
                      "Transfer-Encoding: chunked\r\n\r\n" +
                      "5\r\n" +
                      "hello\r\n" +
                      "0\r\n\r\n";
        var bytes = Encoding.ASCII.GetBytes(request);

        var outcome = decoder.Feed(bytes, out _);

        Assert.Equal(DecodeOutcome.Complete, outcome);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-7.1")]
    public void Feed_should_accept_chunk_size_with_leading_zeros()
    {
        var decoder = MakeDecoder();
        var request = "POST / HTTP/1.1\r\n" +
                      "Host: example.com\r\n" +
                      "Transfer-Encoding: chunked\r\n\r\n" +
                      "0005\r\n" +
                      "hello\r\n" +
                      "0\r\n\r\n";
        var bytes = Encoding.ASCII.GetBytes(request);

        var outcome = decoder.Feed(bytes, out _);

        Assert.Equal(DecodeOutcome.Complete, outcome);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-7.6.1")]
    public void HasConnectionClose_should_detect_close_case_insensitive()
    {
        var decoder = MakeDecoder();
        var request = "GET / HTTP/1.1\r\n" +
                      "Host: example.com\r\n" +
                      "Connection: CLOSE\r\n" +
                      "Content-Length: 0\r\n\r\n";
        var bytes = Encoding.ASCII.GetBytes(request);

        _ = decoder.Feed(bytes, out _);

        Assert.True(decoder.HasConnectionClose);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-7.6.1")]
    public void HasConnectionClose_should_be_false_for_keep_alive()
    {
        var decoder = MakeDecoder();
        var request = "GET / HTTP/1.1\r\n" +
                      "Host: example.com\r\n" +
                      "Connection: keep-alive\r\n" +
                      "Content-Length: 0\r\n\r\n";
        var bytes = Encoding.ASCII.GetBytes(request);

        _ = decoder.Feed(bytes, out _);

        Assert.False(decoder.HasConnectionClose);
    }

    [Fact(Timeout = 5000)]
    public void Reset_should_be_idempotent()
    {
        var decoder = MakeDecoder();
        var request = "GET / HTTP/1.1\r\n" +
                      "Host: example.com\r\n" +
                      "Content-Length: 0\r\n\r\n";
        var bytes = Encoding.ASCII.GetBytes(request);

        _ = decoder.Feed(bytes, out _);
        decoder.Reset();
        decoder.Reset();

        // Should not crash; if it does, exception will be caught by test framework
        Assert.True(true);
    }

    [Fact(Timeout = 5000)]
    public void Reset_should_allow_decoding_next_request()
    {
        var decoder = MakeDecoder();
        var request1 = "GET /first HTTP/1.1\r\n" +
                       "Host: example.com\r\n" +
                       "Content-Length: 0\r\n\r\n";
        var bytes1 = Encoding.ASCII.GetBytes(request1);

        _ = decoder.Feed(bytes1, out _);
        var msg1 = decoder.GetRequest();

        decoder.Reset();

        var request2 = "POST /second HTTP/1.1\r\n" +
                       "Host: example.com\r\n" +
                       "Content-Length: 0\r\n\r\n";
        var bytes2 = Encoding.ASCII.GetBytes(request2);
        _ = decoder.Feed(bytes2, out _);
        var msg2 = decoder.GetRequest();

        Assert.Equal(HttpMethod.Get, msg1.Method);
        Assert.Equal("/first", msg1.RequestUri?.OriginalString);
        Assert.Equal(HttpMethod.Post, msg2.Method);
        Assert.Equal("/second", msg2.RequestUri?.OriginalString);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-5.2")]
    public void Feed_should_reject_obs_fold_when_not_allowed()
    {
        var shared = new SharedHttpOptions { AllowObsFold = false };
        var decoder = MakeDecoder(shared);
        var request = "GET / HTTP/1.1\r\n" +
                      "Host: example.com\r\n" +
                      "X-Custom: value\r\n" +
                      " continued\r\n" +
                      "Content-Length: 0\r\n\r\n";
        var bytes = Encoding.ASCII.GetBytes(request);

        _ = Assert.Throws<HttpProtocolException>(() => decoder.Feed(bytes, out _));
    }

    [Fact(Timeout = 5000)]
    public void Feed_should_not_crash_after_prior_error()
    {
        var decoder = MakeDecoder();
        var badRequest = "POST / HTTP/1.1\r\n" +
                         "Host: example.com\r\n" +
                         "Content-Length: abc\r\n\r\n";
        var badBytes = Encoding.ASCII.GetBytes(badRequest);

        // Feed invalid request
        _ = Record.Exception(() => decoder.Feed(badBytes, out _));

        // Reset and feed valid request
        decoder.Reset();
        var validRequest = "GET / HTTP/1.1\r\n" +
                           "Host: example.com\r\n" +
                           "Content-Length: 0\r\n\r\n";
        var validBytes = Encoding.ASCII.GetBytes(validRequest);

        var exception = Record.Exception(() => decoder.Feed(validBytes, out _));

        // Should not throw NullReferenceException or other crashes
        Assert.Null(exception);
    }
}
