using System.Net;
using TurboHttp.Protocol;

namespace TurboHttp.Tests.Integration;

/// <summary>
/// Tests for <see cref="ConnectionReuseEvaluator"/>.
/// RFC 9112 §9 — Persistent Connections.
/// </summary>
public sealed class ConnectionReuseEvaluatorTests
{
    // ── HTTP/1.0 — default is close ──────────────────────────────────────────────

    [Fact(DisplayName = "CM-001: Should_Close_When_Http10_And_No_Connection_Header")]
    public void Should_Close_When_Http10_And_No_Connection_Header()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        var decision = ConnectionReuseEvaluator.Evaluate(response, HttpVersion.Version10);
        Assert.False(decision.CanReuse);
        Assert.Contains("not persistent by default", decision.Reason);
    }

    [Fact(DisplayName = "CM-002: Should_KeepAlive_When_Http10_And_Connection_KeepAlive")]
    public void Should_KeepAlive_When_Http10_And_Connection_KeepAlive()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        response.Headers.Connection.Add("Keep-Alive");
        var decision = ConnectionReuseEvaluator.Evaluate(response, HttpVersion.Version10);
        Assert.True(decision.CanReuse);
    }

    [Fact(DisplayName = "CM-003: Should_KeepAlive_When_Http10_And_Connection_Keep_Alive_Lowercase")]
    public void Should_KeepAlive_When_Http10_And_Connection_Keep_Alive_Lowercase()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        response.Headers.Connection.Add("keep-alive");
        var decision = ConnectionReuseEvaluator.Evaluate(response, HttpVersion.Version10);
        Assert.True(decision.CanReuse);
    }

    [Fact(DisplayName = "CM-004: Should_KeepAlive_When_Http10_And_Connection_KEEP_ALIVE_Uppercase")]
    public void Should_KeepAlive_When_Http10_And_Connection_KEEP_ALIVE_Uppercase()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        response.Headers.Connection.Add("KEEP-ALIVE");
        var decision = ConnectionReuseEvaluator.Evaluate(response, HttpVersion.Version10);
        Assert.True(decision.CanReuse);
    }

    [Fact(DisplayName = "CM-005: Should_Close_When_Http10_And_Connection_Close")]
    public void Should_Close_When_Http10_And_Connection_Close()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        response.Headers.Connection.Add("close");
        var decision = ConnectionReuseEvaluator.Evaluate(response, HttpVersion.Version10);
        Assert.False(decision.CanReuse);
        Assert.Contains("Connection: close", decision.Reason);
    }

    // ── HTTP/1.1 — default is keep-alive ────────────────────────────────────────

    [Fact(DisplayName = "CM-006: Should_KeepAlive_When_Http11_And_No_Connection_Header")]
    public void Should_KeepAlive_When_Http11_And_No_Connection_Header()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        var decision = ConnectionReuseEvaluator.Evaluate(response, HttpVersion.Version11);
        Assert.True(decision.CanReuse);
        Assert.Contains("persistent connection", decision.Reason);
    }

    [Fact(DisplayName = "CM-007: Should_Close_When_Http11_And_Connection_Close")]
    public void Should_Close_When_Http11_And_Connection_Close()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        response.Headers.Connection.Add("close");
        var decision = ConnectionReuseEvaluator.Evaluate(response, HttpVersion.Version11);
        Assert.False(decision.CanReuse);
        Assert.Contains("Connection: close", decision.Reason);
    }

    [Fact(DisplayName = "CM-008: Should_Close_When_Http11_And_Connection_Close_Uppercase")]
    public void Should_Close_When_Http11_And_Connection_Close_Uppercase()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        response.Headers.Connection.Add("Close");
        var decision = ConnectionReuseEvaluator.Evaluate(response, HttpVersion.Version11);
        Assert.False(decision.CanReuse);
    }

    [Fact(DisplayName = "CM-009: Should_KeepAlive_When_Http11_And_Connection_KeepAlive_Header")]
    public void Should_KeepAlive_When_Http11_And_Connection_KeepAlive_Header()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        response.Headers.Connection.Add("keep-alive");
        var decision = ConnectionReuseEvaluator.Evaluate(response, HttpVersion.Version11);
        Assert.True(decision.CanReuse);
    }

    // ── Keep-Alive header parameter parsing ─────────────────────────────────────

    [Fact(DisplayName = "CM-010: Should_ParseTimeout_When_Http11_And_KeepAlive_Timeout")]
    public void Should_ParseTimeout_When_Http11_And_KeepAlive_Timeout()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        response.Headers.TryAddWithoutValidation("Keep-Alive", "timeout=5");
        var decision = ConnectionReuseEvaluator.Evaluate(response, HttpVersion.Version11);
        Assert.True(decision.CanReuse);
        Assert.Equal(TimeSpan.FromSeconds(5), decision.KeepAliveTimeout);
        Assert.Null(decision.MaxRequests);
    }

    [Fact(DisplayName = "CM-011: Should_ParseTimeoutAndMax_When_Http11_And_KeepAlive_Both_Params")]
    public void Should_ParseTimeoutAndMax_When_Http11_And_KeepAlive_Both_Params()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        response.Headers.TryAddWithoutValidation("Keep-Alive", "timeout=30, max=100");
        var decision = ConnectionReuseEvaluator.Evaluate(response, HttpVersion.Version11);
        Assert.True(decision.CanReuse);
        Assert.Equal(TimeSpan.FromSeconds(30), decision.KeepAliveTimeout);
        Assert.Equal(100, decision.MaxRequests);
    }

    [Fact(DisplayName = "CM-012: Should_ParseTimeout_When_Http10_KeepAlive_With_Timeout_Param")]
    public void Should_ParseTimeout_When_Http10_KeepAlive_With_Timeout_Param()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        response.Headers.Connection.Add("Keep-Alive");
        response.Headers.TryAddWithoutValidation("Keep-Alive", "timeout=10");
        var decision = ConnectionReuseEvaluator.Evaluate(response, HttpVersion.Version10);
        Assert.True(decision.CanReuse);
        Assert.Equal(TimeSpan.FromSeconds(10), decision.KeepAliveTimeout);
    }

    [Fact(DisplayName = "CM-013: Should_IgnoreInvalidTimeout_When_KeepAlive_Has_Non_Numeric_Timeout")]
    public void Should_IgnoreInvalidTimeout_When_KeepAlive_Has_Non_Numeric_Timeout()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        response.Headers.TryAddWithoutValidation("Keep-Alive", "timeout=abc");
        var decision = ConnectionReuseEvaluator.Evaluate(response, HttpVersion.Version11);
        Assert.True(decision.CanReuse);
        Assert.Null(decision.KeepAliveTimeout);
    }

    [Fact(DisplayName = "CM-014: Should_ParseMax_When_KeepAlive_Has_Max_Only")]
    public void Should_ParseMax_When_KeepAlive_Has_Max_Only()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        response.Headers.TryAddWithoutValidation("Keep-Alive", "max=50");
        var decision = ConnectionReuseEvaluator.Evaluate(response, HttpVersion.Version11);
        Assert.True(decision.CanReuse);
        Assert.Null(decision.KeepAliveTimeout);
        Assert.Equal(50, decision.MaxRequests);
    }

    // ── Body / error flags ───────────────────────────────────────────────────────

    [Fact(DisplayName = "CM-015: Should_Close_When_Http11_And_Body_Not_Fully_Consumed")]
    public void Should_Close_When_Http11_And_Body_Not_Fully_Consumed()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        var decision = ConnectionReuseEvaluator.Evaluate(
            response, HttpVersion.Version11, bodyFullyConsumed: false);
        Assert.False(decision.CanReuse);
        Assert.Contains("body not fully consumed", decision.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(DisplayName = "CM-016: Should_Close_When_Http11_And_Protocol_Error_Occurred")]
    public void Should_Close_When_Http11_And_Protocol_Error_Occurred()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        var decision = ConnectionReuseEvaluator.Evaluate(
            response, HttpVersion.Version11, protocolErrorOccurred: true);
        Assert.False(decision.CanReuse);
        Assert.Contains("Protocol error", decision.Reason);
    }

    [Fact(DisplayName = "CM-017: Should_Close_On_ProtocolError_Even_When_ConnectionClose_Not_Set")]
    public void Should_Close_On_ProtocolError_Even_When_ConnectionClose_Not_Set()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        response.Headers.Connection.Add("keep-alive");
        var decision = ConnectionReuseEvaluator.Evaluate(
            response, HttpVersion.Version11, protocolErrorOccurred: true);
        Assert.False(decision.CanReuse);
    }

    // ── Status code: 101 Switching Protocols ────────────────────────────────────

    [Fact(DisplayName = "CM-018: Should_Close_When_101_Switching_Protocols")]
    public void Should_Close_When_101_Switching_Protocols()
    {
        var response = new HttpResponseMessage(HttpStatusCode.SwitchingProtocols);
        var decision = ConnectionReuseEvaluator.Evaluate(response, HttpVersion.Version11);
        Assert.False(decision.CanReuse);
        Assert.Contains("101", decision.Reason);
    }

    // ── HTTP/2 — always keep-alive at this layer ─────────────────────────────────

    [Fact(DisplayName = "CM-019: Should_KeepAlive_When_Http2_No_Headers")]
    public void Should_KeepAlive_When_Http2_No_Headers()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        var decision = ConnectionReuseEvaluator.Evaluate(response, HttpVersion.Version20);
        Assert.True(decision.CanReuse);
        Assert.Contains("multiplexed", decision.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(DisplayName = "CM-020: Should_KeepAlive_When_Http2_Body_Not_Consumed")]
    public void Should_KeepAlive_When_Http2_Body_Not_Consumed()
    {
        // HTTP/2 stream close != connection close; the evaluator always returns keep-alive
        // for HTTP/2 and lets the I/O layer handle connection-level errors separately.
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        var decision = ConnectionReuseEvaluator.Evaluate(
            response, HttpVersion.Version20, bodyFullyConsumed: false);
        Assert.True(decision.CanReuse);
    }

    [Fact(DisplayName = "CM-021: Should_KeepAlive_When_Http2_Protocol_Error_Occurred")]
    public void Should_KeepAlive_When_Http2_Protocol_Error_Occurred()
    {
        // The I/O layer handles HTTP/2 connection errors (GOAWAY); this evaluator does not.
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        var decision = ConnectionReuseEvaluator.Evaluate(
            response, HttpVersion.Version20, protocolErrorOccurred: true);
        Assert.True(decision.CanReuse);
    }

    [Fact(DisplayName = "CM-022: Should_KeepAlive_When_Http2_Even_If_Connection_Close_Present")]
    public void Should_KeepAlive_When_Http2_Even_If_Connection_Close_Present()
    {
        // RFC 9113 §8.2.2: Connection-specific headers MUST NOT be forwarded in HTTP/2.
        // The evaluator returns keep-alive before inspecting Connection headers for HTTP/2.
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        response.Headers.Connection.Add("close");
        var decision = ConnectionReuseEvaluator.Evaluate(response, HttpVersion.Version20);
        Assert.True(decision.CanReuse);
    }

    // ── Reason string quality ────────────────────────────────────────────────────

    [Fact(DisplayName = "CM-023: Should_Have_NonEmpty_Reason_On_KeepAlive")]
    public void Should_Have_NonEmpty_Reason_On_KeepAlive()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        var decision = ConnectionReuseEvaluator.Evaluate(response, HttpVersion.Version11);
        Assert.NotEmpty(decision.Reason);
    }

    [Fact(DisplayName = "CM-024: Should_Have_NonEmpty_Reason_On_Close")]
    public void Should_Have_NonEmpty_Reason_On_Close()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        response.Headers.Connection.Add("close");
        var decision = ConnectionReuseEvaluator.Evaluate(response, HttpVersion.Version11);
        Assert.NotEmpty(decision.Reason);
    }

    [Fact(DisplayName = "CM-025: Should_HaveNullTimeouts_When_Http11_No_KeepAlive_Header")]
    public void Should_HaveNullTimeouts_When_Http11_No_KeepAlive_Header()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        var decision = ConnectionReuseEvaluator.Evaluate(response, HttpVersion.Version11);
        Assert.True(decision.CanReuse);
        Assert.Null(decision.KeepAliveTimeout);
        Assert.Null(decision.MaxRequests);
    }
}
