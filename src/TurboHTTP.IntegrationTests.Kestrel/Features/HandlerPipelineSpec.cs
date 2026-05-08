using System.Net;
using TurboHTTP.IntegrationTests.Kestrel.Shared;

namespace TurboHTTP.IntegrationTests.Kestrel.Features;

public sealed class HandlerPipelineSpec : FeatureSpecBase
{
    public HandlerPipelineSpec(ServerFixture server, ActorSystemFixture systemFixture)
        : base(server, systemFixture) { }

    [Theory(Timeout = 15000)]
    [MemberData(nameof(Protocols))]
    public async Task Handler_should_inject_custom_header_with_use_request(HttpProtocol protocol)
    {
        await using var helper = CreateClient(protocol, b => b
            .UseRequest(req =>
            {
                req.Headers.Add("X-Injected", "from-handler");
                return req;
            }));
        var ct = TestContext.Current.CancellationToken;

        var response = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/headers/echo"), ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.TryGetValues("X-Injected", out var values));
        Assert.Contains("from-handler", values);
    }

    [Theory(Timeout = 15000)]
    [MemberData(nameof(Protocols))]
    public async Task Handler_should_add_header_to_response(HttpProtocol protocol)
    {
        await using var helper = CreateClient(protocol, b => b
            .UseResponse((req, resp) =>
            {
                resp.Headers.Add("X-Processed", "true");
                return resp;
            }));
        var ct = TestContext.Current.CancellationToken;

        var response = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/hello"), ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.TryGetValues("X-Processed", out var values));
        Assert.Contains("true", values);
    }

    [Theory(Timeout = 15000)]
    [MemberData(nameof(Protocols))]
    public async Task Handler_should_execute_multiple_handlers_in_order(HttpProtocol protocol)
    {
        await using var helper = CreateClient(protocol, b => b
            .UseRequest(req =>
            {
                req.Headers.Add("X-First", "1");
                return req;
            })
            .UseRequest(req =>
            {
                req.Headers.Add("X-Second", "2");
                return req;
            }));
        var ct = TestContext.Current.CancellationToken;

        var response = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/headers/echo"), ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.TryGetValues("X-First", out _));
        Assert.True(response.Headers.TryGetValues("X-Second", out _));
    }

    [Theory(Timeout = 15000)]
    [MemberData(nameof(Protocols))]
    public async Task Handler_should_work_with_redirect(HttpProtocol protocol)
    {
        await using var helper = CreateClient(protocol, b => b
            .WithRedirect()
            .UseRequest(req =>
            {
                req.Headers.Add("X-Handler", "active");
                return req;
            }));
        var ct = TestContext.Current.CancellationToken;

        var response = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/redirect/302/headers/echo"), ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.TryGetValues("X-Handler", out var values));
        Assert.Contains("active", values);
    }

    [Theory(Timeout = 15000)]
    [MemberData(nameof(Protocols))]
    public async Task Handler_should_work_with_decompression(HttpProtocol protocol)
    {
        await using var helper = CreateClient(protocol, b => b
            .WithDecompression()
            .UseRequest(req =>
            {
                req.Headers.Add("X-Handler", "active");
                return req;
            }));
        var ct = TestContext.Current.CancellationToken;

        var response = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/compress/gzip/1"), ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsByteArrayAsync(ct);
        Assert.Equal(1024, content.Length);
    }
}
