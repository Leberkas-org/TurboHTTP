using System.Net;
using TurboHttp.Protocol.Http11;

namespace TurboHttp.Tests.Http11;

/// <summary>
/// Tests connection reuse evaluation logic per RFC 9112 §9.
/// Verifies keep-alive/close decisions based on HTTP version and Connection header.
/// </summary>
/// <remarks>
/// Class under test: <see cref="ConnectionReuseEvaluator"/>.
/// RFC 9112 §9: Connection persistence — HTTP/1.1 persistent by default; HTTP/1.0 not.
/// </remarks>
public sealed class Http11ConnectionReuseSpec
{
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9")]
    public void Http11ConnectionReuse_should_close_when_http10_and_no_connection_header()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        var decision = ConnectionReuseEvaluator.Evaluate(response, HttpVersion.Version10);
        Assert.False(decision.CanReuse);
        Assert.Contains("not persistent by default", decision.Reason);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9")]
    public void Http11ConnectionReuse_should_keep_alive_when_http10_and_connection_keep_alive()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        response.Headers.Connection.Add("Keep-Alive");
        var decision = ConnectionReuseEvaluator.Evaluate(response, HttpVersion.Version10);
        Assert.True(decision.CanReuse);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9")]
    public void Http11ConnectionReuse_should_keep_alive_when_http10_and_connection_keep_alive_lowercase()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        response.Headers.Connection.Add("keep-alive");
        var decision = ConnectionReuseEvaluator.Evaluate(response, HttpVersion.Version10);
        Assert.True(decision.CanReuse);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9")]
    public void Http11ConnectionReuse_should_keep_alive_when_http10_and_connection_keep_alive_uppercase()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        response.Headers.Connection.Add("KEEP-ALIVE");
        var decision = ConnectionReuseEvaluator.Evaluate(response, HttpVersion.Version10);
        Assert.True(decision.CanReuse);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9")]
    public void Http11ConnectionReuse_should_close_when_http10_and_connection_close()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        response.Headers.Connection.Add("close");
        var decision = ConnectionReuseEvaluator.Evaluate(response, HttpVersion.Version10);
        Assert.False(decision.CanReuse);
        Assert.Contains("Connection: close", decision.Reason);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9")]
    public void Http11ConnectionReuse_should_keep_alive_when_http11_and_no_connection_header()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        var decision = ConnectionReuseEvaluator.Evaluate(response, HttpVersion.Version11);
        Assert.True(decision.CanReuse);
        Assert.Contains("persistent connection", decision.Reason);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9")]
    public void Http11ConnectionReuse_should_close_when_http11_and_connection_close()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        response.Headers.Connection.Add("close");
        var decision = ConnectionReuseEvaluator.Evaluate(response, HttpVersion.Version11);
        Assert.False(decision.CanReuse);
        Assert.Contains("Connection: close", decision.Reason);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9")]
    public void Http11ConnectionReuse_should_close_when_http11_and_connection_close_uppercase()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        response.Headers.Connection.Add("Close");
        var decision = ConnectionReuseEvaluator.Evaluate(response, HttpVersion.Version11);
        Assert.False(decision.CanReuse);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9")]
    public void Http11ConnectionReuse_should_keep_alive_when_http11_and_connection_keep_alive_header()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        response.Headers.Connection.Add("keep-alive");
        var decision = ConnectionReuseEvaluator.Evaluate(response, HttpVersion.Version11);
        Assert.True(decision.CanReuse);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9")]
    public void Http11ConnectionReuse_should_parse_timeout_when_http11_and_keep_alive_timeout()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        response.Headers.TryAddWithoutValidation("Keep-Alive", "timeout=5");
        var decision = ConnectionReuseEvaluator.Evaluate(response, HttpVersion.Version11);
        Assert.True(decision.CanReuse);
        Assert.Equal(TimeSpan.FromSeconds(5), decision.KeepAliveTimeout);
        Assert.Null(decision.MaxRequests);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9")]
    public void Http11ConnectionReuse_should_parse_timeout_and_max_when_http11_and_keep_alive_both_params()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        response.Headers.TryAddWithoutValidation("Keep-Alive", "timeout=30, max=100");
        var decision = ConnectionReuseEvaluator.Evaluate(response, HttpVersion.Version11);
        Assert.True(decision.CanReuse);
        Assert.Equal(TimeSpan.FromSeconds(30), decision.KeepAliveTimeout);
        Assert.Equal(100, decision.MaxRequests);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9")]
    public void Http11ConnectionReuse_should_parse_timeout_when_http10_keep_alive_with_timeout_param()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        response.Headers.Connection.Add("Keep-Alive");
        response.Headers.TryAddWithoutValidation("Keep-Alive", "timeout=10");
        var decision = ConnectionReuseEvaluator.Evaluate(response, HttpVersion.Version10);
        Assert.True(decision.CanReuse);
        Assert.Equal(TimeSpan.FromSeconds(10), decision.KeepAliveTimeout);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9")]
    public void Http11ConnectionReuse_should_ignore_invalid_timeout_when_keep_alive_has_non_numeric_timeout()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        response.Headers.TryAddWithoutValidation("Keep-Alive", "timeout=abc");
        var decision = ConnectionReuseEvaluator.Evaluate(response, HttpVersion.Version11);
        Assert.True(decision.CanReuse);
        Assert.Null(decision.KeepAliveTimeout);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9")]
    public void Http11ConnectionReuse_should_parse_max_when_keep_alive_has_max_only()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        response.Headers.TryAddWithoutValidation("Keep-Alive", "max=50");
        var decision = ConnectionReuseEvaluator.Evaluate(response, HttpVersion.Version11);
        Assert.True(decision.CanReuse);
        Assert.Null(decision.KeepAliveTimeout);
        Assert.Equal(50, decision.MaxRequests);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9")]
    public void Http11ConnectionReuse_should_close_when_http11_and_body_not_fully_consumed()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        var decision = ConnectionReuseEvaluator.Evaluate(
            response, HttpVersion.Version11, bodyFullyConsumed: false);
        Assert.False(decision.CanReuse);
        Assert.Contains("body not fully consumed", decision.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9")]
    public void Http11ConnectionReuse_should_close_when_http11_and_protocol_error_occurred()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        var decision = ConnectionReuseEvaluator.Evaluate(
            response, HttpVersion.Version11, protocolErrorOccurred: true);
        Assert.False(decision.CanReuse);
        Assert.Contains("Protocol error", decision.Reason);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9")]
    public void Http11ConnectionReuse_should_close_on_protocol_error_even_when_connection_close_not_set()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        response.Headers.Connection.Add("keep-alive");
        var decision = ConnectionReuseEvaluator.Evaluate(
            response, HttpVersion.Version11, protocolErrorOccurred: true);
        Assert.False(decision.CanReuse);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9")]
    public void Http11ConnectionReuse_should_close_when_101_switching_protocols()
    {
        var response = new HttpResponseMessage(HttpStatusCode.SwitchingProtocols);
        var decision = ConnectionReuseEvaluator.Evaluate(response, HttpVersion.Version11);
        Assert.False(decision.CanReuse);
        Assert.Contains("101", decision.Reason);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9")]
    public void Http11ConnectionReuse_should_keep_alive_when_http2_no_headers()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        var decision = ConnectionReuseEvaluator.Evaluate(response, HttpVersion.Version20);
        Assert.True(decision.CanReuse);
        Assert.Contains("multiplexed", decision.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9")]
    public void Http11ConnectionReuse_should_keep_alive_when_http2_body_not_consumed()
    {
        // HTTP/2 stream close != connection close; the evaluator always returns keep-alive
        // for HTTP/2 and lets the I/O layer handle connection-level errors separately.
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        var decision = ConnectionReuseEvaluator.Evaluate(
            response, HttpVersion.Version20, bodyFullyConsumed: false);
        Assert.True(decision.CanReuse);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9")]
    public void Http11ConnectionReuse_should_keep_alive_when_http2_protocol_error_occurred()
    {
        // The I/O layer handles HTTP/2 connection errors (GOAWAY); this evaluator does not.
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        var decision = ConnectionReuseEvaluator.Evaluate(
            response, HttpVersion.Version20, protocolErrorOccurred: true);
        Assert.True(decision.CanReuse);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9")]
    public void Http11ConnectionReuse_should_keep_alive_when_http2_even_if_connection_close_present()
    {
        // RFC 9113 §8.2.2: Connection-specific headers MUST NOT be forwarded in HTTP/2.
        // The evaluator returns keep-alive before inspecting Connection headers for HTTP/2.
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        response.Headers.Connection.Add("close");
        var decision = ConnectionReuseEvaluator.Evaluate(response, HttpVersion.Version20);
        Assert.True(decision.CanReuse);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9")]
    public void Http11ConnectionReuse_should_have_non_empty_reason_on_keep_alive()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        var decision = ConnectionReuseEvaluator.Evaluate(response, HttpVersion.Version11);
        Assert.NotEmpty(decision.Reason);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9")]
    public void Http11ConnectionReuse_should_have_non_empty_reason_on_close()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        response.Headers.Connection.Add("close");
        var decision = ConnectionReuseEvaluator.Evaluate(response, HttpVersion.Version11);
        Assert.NotEmpty(decision.Reason);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9")]
    public void Http11ConnectionReuse_should_have_null_timeouts_when_http11_no_keep_alive_header()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        var decision = ConnectionReuseEvaluator.Evaluate(response, HttpVersion.Version11);
        Assert.True(decision.CanReuse);
        Assert.Null(decision.KeepAliveTimeout);
        Assert.Null(decision.MaxRequests);
    }

    [Theory(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9")]
    [InlineData("1.0")]
    [InlineData("1.1")]
    public void Http11ConnectionReuse_should_deny_reuse_when_body_not_consumed(string version)
    {
        // RFC 9112 §9.3: "If the client intends to reuse the connection, it MUST
        // read the entire response message body."
        var httpVersion = version == "1.0" ? HttpVersion.Version10 : HttpVersion.Version11;
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        if (version == "1.0")
        {
            response.Headers.Connection.Add("Keep-Alive");
        }

        var decision = ConnectionReuseEvaluator.Evaluate(
            response, httpVersion, bodyFullyConsumed: false);

        Assert.False(decision.CanReuse);
        Assert.Contains("body not fully consumed", decision.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Theory(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9")]
    [InlineData("1.0")]
    [InlineData("1.1")]
    public void Http11ConnectionReuse_should_allow_reuse_when_body_fully_consumed(string version)
    {
        // RFC 9112 §9.3: When the body is fully consumed and no close signal
        // is present, the connection is eligible for reuse.
        var httpVersion = version == "1.0" ? HttpVersion.Version10 : HttpVersion.Version11;
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        if (version == "1.0")
        {
            response.Headers.Connection.Add("Keep-Alive");
        }

        var decision = ConnectionReuseEvaluator.Evaluate(
            response, httpVersion, bodyFullyConsumed: true);

        Assert.True(decision.CanReuse);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9")]
    public void Http11ConnectionReuse_should_deny_reuse_for_101_switching_protocols()
    {
        // RFC 9112 §9.6: A 101 response means the connection has been upgraded
        // to a different protocol; it cannot be returned to an HTTP pool.
        var response = new HttpResponseMessage(HttpStatusCode.SwitchingProtocols);
        response.Headers.TryAddWithoutValidation("Upgrade", "websocket");

        var decision = ConnectionReuseEvaluator.Evaluate(response, HttpVersion.Version11);

        Assert.False(decision.CanReuse);
        Assert.Contains("101", decision.Reason);
        Assert.Contains("Switching Protocols", decision.Reason);
    }
}
