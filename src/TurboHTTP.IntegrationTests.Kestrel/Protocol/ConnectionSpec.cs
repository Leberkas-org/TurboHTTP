using System.Net;
using TurboHTTP.IntegrationTests.Kestrel.Shared;

namespace TurboHTTP.IntegrationTests.Kestrel.Protocol;

public sealed class ConnectionSpec : FeatureSpecBase
{
    public ConnectionSpec(ServerFixture server, ActorSystemFixture systemFixture)
        : base(server, systemFixture) { }

    [Theory(Timeout = 15000)]
    [MemberData(nameof(Protocols))]
    public async Task Connection_should_return_body_for_simple_get(HttpProtocol protocol)
    {
        await using var helper = CreateClient(protocol);
        var ct = TestContext.Current.CancellationToken;

        var response = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/hello"), ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Hello World", await response.Content.ReadAsStringAsync(ct));
    }

    [Theory(Timeout = 15000)]
    [MemberData(nameof(Protocols))]
    public async Task Connection_should_handle_sequential_requests(HttpProtocol protocol)
    {
        await using var helper = CreateClient(protocol);
        var ct = TestContext.Current.CancellationToken;

        for (var i = 0; i < 5; i++)
        {
            var response = await helper.Client.SendAsync(
                new HttpRequestMessage(HttpMethod.Get, "/hello"), ct);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    [Theory(Timeout = 15000)]
    [MemberData(nameof(Protocols))]
    public async Task Connection_should_echo_post_body(HttpProtocol protocol)
    {
        await using var helper = CreateClient(protocol);
        var ct = TestContext.Current.CancellationToken;

        var payload = "echo test";
        var response = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Post, "/echo")
            {
                Content = new StringContent(payload)
            }, ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(payload, await response.Content.ReadAsStringAsync(ct));
    }

    [Theory(Timeout = 15000)]
    [MemberData(nameof(Protocols))]
    public async Task Connection_should_echo_put_body(HttpProtocol protocol)
    {
        await using var helper = CreateClient(protocol);
        var ct = TestContext.Current.CancellationToken;

        var payload = "put body";
        var response = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Put, "/echo")
            {
                Content = new StringContent(payload)
            }, ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(payload, await response.Content.ReadAsStringAsync(ct));
    }

    [Theory(Timeout = 15000)]
    [MemberData(nameof(Protocols))]
    public async Task Connection_should_echo_patch_body(HttpProtocol protocol)
    {
        await using var helper = CreateClient(protocol);
        var ct = TestContext.Current.CancellationToken;

        var payload = "patch body";
        var response = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Patch, "/echo")
            {
                Content = new StringContent(payload)
            }, ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(payload, await response.Content.ReadAsStringAsync(ct));
    }

    [Theory(Timeout = 15000)]
    [MemberData(nameof(Protocols))]
    public async Task Connection_should_handle_head_request(HttpProtocol protocol)
    {
        await using var helper = CreateClient(protocol);
        var ct = TestContext.Current.CancellationToken;

        var response = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Head, "/hello"), ct);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }
}
