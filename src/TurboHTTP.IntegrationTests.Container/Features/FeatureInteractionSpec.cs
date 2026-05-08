using System.Net;
using System.Text.Json;
using TurboHTTP.IntegrationTests.Container.Shared;

namespace TurboHTTP.IntegrationTests.Container.Features;

public sealed class FeatureInteractionSpec : FeatureSpecBase
{
    public FeatureInteractionSpec(ServerContainerFixture server, ActorSystemFixture systemFixture)
        : base(server, systemFixture) { }

    [Theory(Timeout = 15000)]
    [MemberData(nameof(Protocols))]
    public async Task CookiesAndRedirect_should_set_cookies_via_redirect(HttpProtocol protocol)
    {
        await using var helper = CreateClient(protocol, b => b
            .WithCookies()
            .WithRedirect());
        var ct = TestContext.Current.CancellationToken;

        var response = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/cookies/set?tracking=xyz"), ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync(ct);
        var json = JsonDocument.Parse(body);
        Assert.Equal("xyz", json.RootElement.GetProperty("tracking").GetString());
    }

    [Theory(Timeout = 15000)]
    [MemberData(nameof(Protocols))]
    public async Task DecompressionAndRedirect_should_decompress_after_redirect(HttpProtocol protocol)
    {
        await using var helper = CreateClient(protocol, b => b
            .WithDecompression()
            .WithRedirect());
        var ct = TestContext.Current.CancellationToken;

        var targetUrl = $"{helper.Client.BaseAddress}gzip";
        var response = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get,
                $"/redirect-to?url={Uri.EscapeDataString(targetUrl)}"), ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(ct);
        var json = JsonDocument.Parse(body);
        Assert.True(json.RootElement.GetProperty("gzipped").GetBoolean());
    }

    [Theory(Timeout = 15000)]
    [MemberData(nameof(Protocols))]
    public async Task AuthAndRedirect_should_authenticate_after_redirect(HttpProtocol protocol)
    {
        await using var helper = CreateClient(protocol,
            b => b.WithRedirect(),
            configureOptions: opts =>
            {
                opts.Credentials = new NetworkCredential("user", "pass");
                opts.PreAuthenticate = true;
            });
        var ct = TestContext.Current.CancellationToken;

        var targetUrl = $"{helper.Client.BaseAddress}basic-auth/user/pass";
        var response = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get,
                $"/redirect-to?url={Uri.EscapeDataString(targetUrl)}"), ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory(Timeout = 15000)]
    [MemberData(nameof(Protocols))]
    public async Task AllFeatures_should_work_together(HttpProtocol protocol)
    {
        await using var helper = CreateClient(protocol, b => b
            .WithCookies()
            .WithRedirect()
            .WithDecompression()
            .WithCache());
        var ct = TestContext.Current.CancellationToken;

        var setResponse = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/cookies/set?feature=all"), ct);
        Assert.Equal(HttpStatusCode.OK, setResponse.StatusCode);

        var gzipResponse = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/gzip"), ct);

        Assert.Equal(HttpStatusCode.OK, gzipResponse.StatusCode);
        var body = await gzipResponse.Content.ReadAsStringAsync(ct);
        var json = JsonDocument.Parse(body);
        Assert.True(json.RootElement.GetProperty("gzipped").GetBoolean());
    }
}
