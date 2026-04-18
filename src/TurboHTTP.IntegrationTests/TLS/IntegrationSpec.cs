using System.Net;
using System.Text;
using System.Text.Json;
using TurboHTTP.IntegrationTests.Shared;

namespace TurboHTTP.IntegrationTests.TLS;

[Collection("TLS")]
[Obsolete("Replaced by StreamTests.Acceptance.TLS.IntegrationSpec")]
public sealed class IntegrationSpec
{
    private readonly ServerFixture _server;
    private readonly ActorSystemFixture _systemFixture;

    public IntegrationSpec(ServerFixture server, ActorSystemFixture systemFixture)
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


    [Fact(Timeout = 20000)]
    public async Task Get_hello_should_return_200_over_https()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/hello");
        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(cts.Token);
        Assert.Equal("Hello World", body);
    }

    [Fact(Timeout = 20000)]
    public async Task Post_echo_should_echo_body_over_https()
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


    [Fact(Timeout = 20000)]
    public async Task Headers_should_roundtrip_custom_headers_over_https()
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


    [Fact(Timeout = 20000)]
    public async Task Cookie_should_set_and_echo_roundtrip_over_https()
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

    [Fact(Timeout = 20000)]
    public async Task Secure_cookie_should_be_sent_over_https()
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


    [Fact(Timeout = 20000)]
    public async Task Gzip_should_be_transparently_decompressed_over_https()
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


    [Fact(Timeout = 20000)]
    public async Task Redirect_302_should_be_followed_over_https()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = CreateClient(builder => builder.WithRedirect());

        var request = new HttpRequestMessage(HttpMethod.Get, "/redirect/302/hello");
        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(cts.Token);
        Assert.Equal("Hello World", body);
    }


    [Theory(Timeout = 5000)]
    [InlineData(64)]
    [InlineData(256)]
    public async Task Large_body_should_transfer_over_https(int kb)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Get, $"/large/{kb}");
        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsByteArrayAsync(cts.Token);
        Assert.Equal(kb * 1024, body.Length);
    }


    [Fact(Timeout = 20000)]
    public async Task Chunked_transfer_encoding_should_work_over_https()
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
