using System.Net;
using TurboHTTP.Protocol.Syntax.Http11;

namespace TurboHTTP.Tests.Protocol.Syntax.Http11;

[Trait("RFC", "RFC9112")]
public sealed class ConnectionReuseEvaluatorSpec
{
    [Fact(Timeout = 5000)]
    public void ProtocolError_ShouldClose()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        var decision = ConnectionReuseEvaluator.Evaluate(response, HttpVersion.Version11, protocolErrorOccurred: true);

        Assert.False(decision.CanReuse);
        Assert.Contains("Protocol error", decision.Reason);
    }

    [Fact(Timeout = 5000)]
    public void BodyNotConsumed_ShouldClose()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        var decision = ConnectionReuseEvaluator.Evaluate(response, HttpVersion.Version11, bodyFullyConsumed: false);

        Assert.False(decision.CanReuse);
        Assert.Contains("not fully consumed", decision.Reason);
    }

    [Fact(Timeout = 5000)]
    public void SwitchingProtocols_ShouldClose()
    {
        var response = new HttpResponseMessage((HttpStatusCode)101);
        var decision = ConnectionReuseEvaluator.Evaluate(response, HttpVersion.Version11);

        Assert.False(decision.CanReuse);
        Assert.Contains("101 Switching Protocols", decision.Reason);
    }

    [Fact(Timeout = 5000)]
    public void ConnectionClose_ShouldClose()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        response.Headers.Connection.Add("close");
        var decision = ConnectionReuseEvaluator.Evaluate(response, HttpVersion.Version11);

        Assert.False(decision.CanReuse);
        Assert.Contains("Connection: close", decision.Reason);
    }

    [Fact(Timeout = 5000)]
    public void Http10WithoutKeepAlive_ShouldClose()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        var decision = ConnectionReuseEvaluator.Evaluate(response, HttpVersion.Version10);

        Assert.False(decision.CanReuse);
        Assert.Contains("HTTP/1.0", decision.Reason);
    }

    [Fact(Timeout = 5000)]
    public void Http10WithKeepAlive_ShouldKeepAlive()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        response.Headers.Connection.Add("Keep-Alive");
        var decision = ConnectionReuseEvaluator.Evaluate(response, HttpVersion.Version10);

        Assert.True(decision.CanReuse);
        Assert.Contains("HTTP/1.0", decision.Reason);
    }

    [Fact(Timeout = 5000)]
    public void Http11Default_ShouldKeepAlive()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        var decision = ConnectionReuseEvaluator.Evaluate(response, HttpVersion.Version11);

        Assert.True(decision.CanReuse);
        Assert.Contains("HTTP/1.1", decision.Reason);
    }

    [Fact(Timeout = 5000)]
    public void Http11WithClose_ShouldClose()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        response.Headers.Connection.Add("close");
        var decision = ConnectionReuseEvaluator.Evaluate(response, HttpVersion.Version11);

        Assert.False(decision.CanReuse);
        Assert.Contains("Connection: close", decision.Reason);
    }

    [Fact(Timeout = 5000)]
    public void Http20_ShouldAlwaysKeepAlive()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        var decision = ConnectionReuseEvaluator.Evaluate(response, HttpVersion.Version20);

        Assert.True(decision.CanReuse);
        Assert.Contains("HTTP/2", decision.Reason);
    }
}