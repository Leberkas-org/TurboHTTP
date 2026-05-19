using System.Net;
using System.Text.Json;
using TurboHTTP.Client;
using TurboHTTP.IntegrationTests.Shared;

namespace TurboHTTP.IntegrationTests.Features;

public sealed class FeatureInteractionSpec : FeatureSpecBase
{
    public FeatureInteractionSpec(ServerContainerFixture server, ActorSystemFixture systemFixture)
        : base(server, systemFixture)
    {
    }

    [Theory(Timeout = 15000)]
    [MemberData(nameof(AllVariants))]
    public async Task CookiesAndRedirect_should_set_cookies_via_redirect(ProtocolVariant variant)
    {
        await using var helper = CreateClient(variant, b => b
            .WithCookies()
            .WithRedirect());

        var response = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/cookies/set?tracking=xyz"), CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        var json = JsonDocument.Parse(body);
        var cookies = json.RootElement.GetProperty("cookies");
        Assert.Equal("xyz", cookies.GetProperty("tracking").GetString());
    }

    [Theory(Timeout = 15000)]
    [MemberData(nameof(AllVariants))]
    public async Task DecompressionAndRedirect_should_decompress_after_redirect(ProtocolVariant variant)
    {
        await using var helper = CreateClient(variant, b => b
            .WithDecompression()
            .WithRedirect());

        var targetUrl = $"{helper.Client.BaseAddress}gzip";
        var response = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get,
                $"/redirect-to?url={Uri.EscapeDataString(targetUrl)}"), CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        var json = JsonDocument.Parse(body);
        Assert.True(json.RootElement.GetProperty("gzipped").GetBoolean());
    }

    [Theory(Timeout = 15000)]
    [MemberData(nameof(AllVariants))]
    public async Task AuthAndRedirect_should_authenticate_after_redirect(ProtocolVariant variant)
    {
        await using var helper = CreateClient(variant,
            b => b.WithRedirect(),
            configureOptions: opts =>
            {
                opts.Credentials = new NetworkCredential("user", "pass");
                opts.PreAuthenticate = true;
            });

        var targetUrl = $"{helper.Client.BaseAddress}basic-auth/user/pass";
        var response = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get,
                $"/redirect-to?url={Uri.EscapeDataString(targetUrl)}"), CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory(Timeout = 15000)]
    [MemberData(nameof(AllVariants))]
    public async Task AllFeatures_should_work_together(ProtocolVariant variant)
    {
        await using var helper = CreateClient(variant, b => b
            .WithCookies()
            .WithRedirect()
            .WithDecompression()
            .WithCache());

        var setResponse = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/cookies/set?feature=all"), CancellationToken);
        Assert.Equal(HttpStatusCode.OK, setResponse.StatusCode);

        var gzipResponse = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/gzip"), CancellationToken);

        Assert.Equal(HttpStatusCode.OK, gzipResponse.StatusCode);
        var body = await gzipResponse.Content.ReadAsStringAsync(CancellationToken);
        var json = JsonDocument.Parse(body);
        Assert.True(json.RootElement.GetProperty("gzipped").GetBoolean());
    }
}