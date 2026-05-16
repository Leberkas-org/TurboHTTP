using TurboHTTP.Protocol.Semantics;
using TurboHTTP.Protocol.Syntax;

namespace TurboHTTP.Tests.Protocol.Syntax.Http10;

public sealed class HeaderRouterSpec
{
    [Fact(Timeout = 5000)]
    public void Apply_should_route_content_headers_to_content()
    {
        var parsed = new HeaderCollection
        {
            { "Content-Type", "text/plain" },
            { "Content-Length", "5" },
            { "X-Custom", "value" }
        };

        var msg = new HttpResponseMessage { Content = new ByteArrayContent([]) };
        HeaderRouter.ApplyToResponse(msg, parsed);

        Assert.True(msg.Headers.Contains("X-Custom"));
        Assert.Equal("text/plain", msg.Content.Headers.ContentType?.ToString());
        Assert.Equal(5, msg.Content.Headers.ContentLength);
    }

    [Fact(Timeout = 5000)]
    public void Apply_should_include_hop_by_hop_headers()
    {
        var parsed = new HeaderCollection
        {
            { "Connection", "close" },
            { "Keep-Alive", "timeout=5" },
            { "X-Custom", "value" }
        };

        var msg = new HttpResponseMessage { Content = new ByteArrayContent([]) };
        HeaderRouter.ApplyToResponse(msg, parsed);

        Assert.True(msg.Headers.Contains("Connection"));
        Assert.True(msg.Headers.Contains("Keep-Alive"));
        Assert.True(msg.Headers.Contains("X-Custom"));
    }

    [Fact(Timeout = 5000)]
    public void ApplyToRequest_should_route_content_headers()
    {
        var parsed = new HeaderCollection
        {
            { "Content-Type", "application/json" },
            { "Accept", "*/*" }
        };

        var msg = new HttpRequestMessage(HttpMethod.Post, "http://example.com")
        {
            Content = new ByteArrayContent([])
        };
        HeaderRouter.ApplyToRequest(msg, parsed);

        Assert.True(msg.Headers.Contains("Accept"));
        Assert.Equal("application/json", msg.Content.Headers.ContentType?.ToString());
    }
}