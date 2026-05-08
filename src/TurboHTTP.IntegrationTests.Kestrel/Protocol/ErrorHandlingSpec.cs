using System.Net;
using TurboHTTP.IntegrationTests.Kestrel.Shared;

namespace TurboHTTP.IntegrationTests.Kestrel.Protocol;

public sealed class ErrorHandlingSpec : FeatureSpecBase
{
    public ErrorHandlingSpec(ServerFixture server, ActorSystemFixture systemFixture)
        : base(server, systemFixture) { }

    [Theory(Timeout = 15000)]
    [MemberData(nameof(Protocols))]
    public async Task ErrorHandling_should_complete_delayed_response(HttpProtocol protocol)
    {
        await using var helper = CreateClient(protocol);
        var ct = TestContext.Current.CancellationToken;

        var response = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/delay/200"), ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory(Timeout = 15000)]
    [MemberData(nameof(Protocols))]
    public async Task ErrorHandling_should_abort_on_timeout_cancellation(HttpProtocol protocol)
    {
        await using var helper = CreateClient(protocol);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<Exception>(async () =>
            await helper.Client.SendAsync(
                new HttpRequestMessage(HttpMethod.Get, "/delay/5000"), cts.Token));
    }

    [Theory(Timeout = 15000)]
    [MemberData(nameof(Protocols))]
    public async Task ErrorHandling_should_handle_unknown_content_encoding(HttpProtocol protocol)
    {
        await using var helper = CreateClient(protocol);
        var ct = TestContext.Current.CancellationToken;

        var response = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/edge/unknown-encoding"), ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory(Timeout = 15000)]
    [MemberData(nameof(Protocols))]
    public async Task ErrorHandling_should_return_4xx_status_codes(HttpProtocol protocol)
    {
        await using var helper = CreateClient(protocol);
        var ct = TestContext.Current.CancellationToken;

        foreach (var code in new[] { 400, 401, 403, 404, 405 })
        {
            var response = await helper.Client.SendAsync(
                new HttpRequestMessage(HttpMethod.Get, $"/status/{code}"), ct);
            Assert.Equal(code, (int)response.StatusCode);
        }
    }

    [Theory(Timeout = 15000)]
    [MemberData(nameof(Protocols))]
    public async Task ErrorHandling_should_return_5xx_status_codes(HttpProtocol protocol)
    {
        await using var helper = CreateClient(protocol);
        var ct = TestContext.Current.CancellationToken;

        foreach (var code in new[] { 500, 502, 503 })
        {
            var response = await helper.Client.SendAsync(
                new HttpRequestMessage(HttpMethod.Get, $"/status/{code}"), ct);
            Assert.Equal(code, (int)response.StatusCode);
        }
    }
}
