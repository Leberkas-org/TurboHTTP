using System.Net;
using System.Text;
using System.Text.Json;
using TurboHttp.IntegrationTests.Shared;

namespace TurboHttp.IntegrationTests.TLS;

[Collection("TlsIntegration")]
public sealed class IntegrationTests
{
    private readonly ServerFixture _server;
    private readonly ActorSystemFixture _systemFixture;

    public IntegrationTests(ServerFixture server, ActorSystemFixture systemFixture)
    {
        _server = server;
        _systemFixture = systemFixture;
    }

    private ClientHelper CreateClient(Action<ITurboHttpClientBuilder>? configure = null)
    {
        return ClientHelper.CreateClient(
            _server.HttpsPort,
            new Version(1, 1),
            scheme: "https",
            configure: configure,
            system: _systemFixture.System);
    }

    // ── Basic HTTPS ──────────────────────────────────────────────────────────

    [Fact(DisplayName = "TLS-001: GET /hello returns 200 over HTTPS")]
    public async Task Get_Hello_Returns_200_Over_Https()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/hello");
        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(cts.Token);
        Assert.Equal("Hello World", body);
    }

    [Fact(DisplayName = "TLS-002: POST /echo echoes body over HTTPS")]
    public async Task Post_Echo_Body_Over_Https()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = CreateClient();

        var payload = "TLS echo payload";
        var request = new HttpRequestMessage(HttpMethod.Post, "/echo")
        {
            Content = new StringContent(payload, Encoding.UTF8, "text/plain")
        };
        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(cts.Token);
        Assert.Equal(payload, body);
    }

    // ── Headers ──────────────────────────────────────────────────────────────

    [Fact(DisplayName = "TLS-003: GET /headers/echo roundtrips custom headers over HTTPS")]
    public async Task Headers_Roundtrip_Over_Https()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/headers/echo");
        request.Headers.Add("X-Custom-Tls", "secure-value");
        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.TryGetValues("X-Custom-Tls", out var values));
        Assert.Equal("secure-value", values!.First());
    }

    // ── Cookies over TLS ─────────────────────────────────────────────────────

    [Fact(DisplayName = "TLS-004: Cookie set and echo roundtrip over HTTPS")]
    public async Task Cookie_Set_And_Echo_Roundtrip_Over_Https()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = CreateClient(builder => builder.WithCookies());

        var setRequest = new HttpRequestMessage(HttpMethod.Get, "/cookie/set/tlssession/encrypted");
        var setResponse = await helper.Client.SendAsync(setRequest, cts.Token);
        Assert.Equal(HttpStatusCode.OK, setResponse.StatusCode);

        var echoRequest = new HttpRequestMessage(HttpMethod.Get, "/cookie/echo");
        var echoResponse = await helper.Client.SendAsync(echoRequest, cts.Token);
        Assert.Equal(HttpStatusCode.OK, echoResponse.StatusCode);

        var json = await echoResponse.Content.ReadAsStringAsync(cts.Token);
        var cookies = JsonSerializer.Deserialize<Dictionary<string, string>>(json)!;
        Assert.Equal("encrypted", cookies["tlssession"]);
    }

    [Fact(DisplayName = "TLS-005: Secure cookie IS sent over HTTPS")]
    public async Task Secure_Cookie_Sent_Over_Https()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = CreateClient(builder => builder.WithCookies());

        var setRequest = new HttpRequestMessage(HttpMethod.Get, "/cookie/set-secure/secret/hidden");
        var setResponse = await helper.Client.SendAsync(setRequest, cts.Token);
        Assert.Equal(HttpStatusCode.OK, setResponse.StatusCode);

        var echoRequest = new HttpRequestMessage(HttpMethod.Get, "/cookie/echo");
        var echoResponse = await helper.Client.SendAsync(echoRequest, cts.Token);
        Assert.Equal(HttpStatusCode.OK, echoResponse.StatusCode);

        var json = await echoResponse.Content.ReadAsStringAsync(cts.Token);
        var cookies = JsonSerializer.Deserialize<Dictionary<string, string>>(json)!;
        Assert.True(cookies.ContainsKey("secret"), "Secure cookie MUST be sent over HTTPS");
        Assert.Equal("hidden", cookies["secret"]);
    }

    // ── Compression over TLS ─────────────────────────────────────────────────

    [Fact(DisplayName = "TLS-006: gzip response transparently decompressed over HTTPS")]
    public async Task Gzip_Decompression_Over_Https()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/compress/gzip/4");
        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsByteArrayAsync(cts.Token);
        Assert.Equal(4 * 1024, body.Length);

        for (var i = 0; i < body.Length; i++)
        {
            Assert.Equal((byte)('A' + i % 26), body[i]);
        }
    }

    // ── Redirect over TLS ────────────────────────────────────────────────────

    [Fact(DisplayName = "TLS-007: 302 redirect followed over HTTPS")]
    public async Task Redirect_302_Followed_Over_Https()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = CreateClient(builder => builder.WithRedirect());

        var request = new HttpRequestMessage(HttpMethod.Get, "/redirect/302/hello");
        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(cts.Token);
        Assert.Equal("Hello World", body);
    }

    // ── Large body over TLS ──────────────────────────────────────────────────

    [Theory(DisplayName = "TLS-008: Large body transfer over HTTPS")]
    [InlineData(64)]
    [InlineData(256)]
    public async Task Large_Body_Transfer_Over_Https(int kb)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Get, $"/large/{kb}");
        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsByteArrayAsync(cts.Token);
        Assert.Equal(kb * 1024, body.Length);
    }

    // ── Chunked transfer over TLS ────────────────────────────────────────────

    [Fact(DisplayName = "TLS-009: Chunked transfer encoding over HTTPS")]
    public async Task Chunked_Transfer_Over_Https()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/chunked/4");
        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsByteArrayAsync(cts.Token);
        Assert.Equal(4 * 1024, body.Length);
    }
}
