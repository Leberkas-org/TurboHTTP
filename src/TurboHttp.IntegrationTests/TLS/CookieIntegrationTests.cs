using System.Net;
using System.Text.Json;
using TurboHttp.IntegrationTests.Shared;

namespace TurboHttp.IntegrationTests.TLS;

[Collection("TLS")]
public sealed class CookieIntegrationTests
{
    private readonly ServerFixture _server;
    private readonly ActorSystemFixture _systemFixture;

    public CookieIntegrationTests(ServerFixture server, ActorSystemFixture systemFixture)
    {
        _server = server;
        _systemFixture = systemFixture;
    }

    private ClientHelper CreateCookieClient()
    {
        return ClientHelper.CreateClient(
            _server.HttpsPort,
            new Version(1, 1),
            scheme: "https",
            configure: builder => builder.WithCookies(),
            system: _systemFixture.System);
    }

    [Fact(DisplayName = "Cookie-TLS-001: Set cookie and echo roundtrip over HTTPS")]
    public async Task Set_Cookie_And_Echo_Roundtrip_Over_Https()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = CreateCookieClient();

        var setRequest = new HttpRequestMessage(HttpMethod.Get, "/cookie/set/session/abc123");
        var setResponse = await helper.Client.SendAsync(setRequest, cts.Token);
        Assert.Equal(HttpStatusCode.OK, setResponse.StatusCode);

        var echoRequest = new HttpRequestMessage(HttpMethod.Get, "/cookie/echo");
        var echoResponse = await helper.Client.SendAsync(echoRequest, cts.Token);
        Assert.Equal(HttpStatusCode.OK, echoResponse.StatusCode);

        var json = await echoResponse.Content.ReadAsStringAsync(cts.Token);
        var cookies = JsonSerializer.Deserialize<Dictionary<string, string>>(json)!;
        Assert.Equal("abc123", cookies["session"]);
    }

    [Fact(DisplayName = "Cookie-TLS-002: Secure cookie IS sent over HTTPS")]
    public async Task Secure_Cookie_Sent_Over_Https()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = CreateCookieClient();

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

    [Fact(DisplayName = "Cookie-TLS-003: HttpOnly cookie is sent on subsequent requests over HTTPS")]
    public async Task HttpOnly_Cookie_Sent_On_Subsequent_Requests_Over_Https()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = CreateCookieClient();

        var setRequest = new HttpRequestMessage(HttpMethod.Get, "/cookie/set-httponly/token/xyz");
        var setResponse = await helper.Client.SendAsync(setRequest, cts.Token);
        Assert.Equal(HttpStatusCode.OK, setResponse.StatusCode);

        var echoRequest = new HttpRequestMessage(HttpMethod.Get, "/cookie/echo");
        var echoResponse = await helper.Client.SendAsync(echoRequest, cts.Token);
        Assert.Equal(HttpStatusCode.OK, echoResponse.StatusCode);

        var json = await echoResponse.Content.ReadAsStringAsync(cts.Token);
        var cookies = JsonSerializer.Deserialize<Dictionary<string, string>>(json)!;
        Assert.Equal("xyz", cookies["token"]);
    }

    [Theory(DisplayName = "Cookie-TLS-004: SameSite cookie with policy is stored and sent over HTTPS")]
    [InlineData("Strict")]
    [InlineData("Lax")]
    [InlineData("None")]
    public async Task SameSite_Cookie_Stored_And_Sent_Over_Https(string policy)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = CreateCookieClient();

        var setRequest = new HttpRequestMessage(HttpMethod.Get, $"/cookie/set-samesite/pref/{policy}/{policy}");
        var setResponse = await helper.Client.SendAsync(setRequest, cts.Token);
        Assert.Equal(HttpStatusCode.OK, setResponse.StatusCode);

        var echoRequest = new HttpRequestMessage(HttpMethod.Get, "/cookie/echo");
        var echoResponse = await helper.Client.SendAsync(echoRequest, cts.Token);
        Assert.Equal(HttpStatusCode.OK, echoResponse.StatusCode);

        var json = await echoResponse.Content.ReadAsStringAsync(cts.Token);
        var cookies = JsonSerializer.Deserialize<Dictionary<string, string>>(json)!;
        Assert.Equal(policy, cookies["pref"]);
    }

    [Fact(DisplayName = "Cookie-TLS-005: Expired cookie not sent after Max-Age elapses over HTTPS")]
    public async Task Expired_Cookie_Not_Sent_Over_Https()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = CreateCookieClient();

        var setRequest = new HttpRequestMessage(HttpMethod.Get, "/cookie/set-expires/temp/value/1");
        var setResponse = await helper.Client.SendAsync(setRequest, cts.Token);
        Assert.Equal(HttpStatusCode.OK, setResponse.StatusCode);

        var echoRequest1 = new HttpRequestMessage(HttpMethod.Get, "/cookie/echo");
        var echoResponse1 = await helper.Client.SendAsync(echoRequest1, cts.Token);
        var json1 = await echoResponse1.Content.ReadAsStringAsync(cts.Token);
        var cookies1 = JsonSerializer.Deserialize<Dictionary<string, string>>(json1)!;
        Assert.Equal("value", cookies1["temp"]);

        await Task.Delay(TimeSpan.FromSeconds(2), cts.Token);

        var echoRequest2 = new HttpRequestMessage(HttpMethod.Get, "/cookie/echo");
        var echoResponse2 = await helper.Client.SendAsync(echoRequest2, cts.Token);
        var json2 = await echoResponse2.Content.ReadAsStringAsync(cts.Token);
        var cookies2 = JsonSerializer.Deserialize<Dictionary<string, string>>(json2)!;
        Assert.False(cookies2.ContainsKey("temp"), "Expired cookie should not be sent");
    }

    [Fact(DisplayName = "Cookie-TLS-006: Domain-scoped cookie is stored over HTTPS")]
    public async Task Domain_Scoped_Cookie_Stored_Over_Https()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = CreateCookieClient();

        var setRequest = new HttpRequestMessage(HttpMethod.Get, "/cookie/set-domain/site/val/127.0.0.1");
        var setResponse = await helper.Client.SendAsync(setRequest, cts.Token);
        Assert.Equal(HttpStatusCode.OK, setResponse.StatusCode);

        var echoRequest = new HttpRequestMessage(HttpMethod.Get, "/cookie/echo");
        var echoResponse = await helper.Client.SendAsync(echoRequest, cts.Token);
        Assert.Equal(HttpStatusCode.OK, echoResponse.StatusCode);

        var json = await echoResponse.Content.ReadAsStringAsync(cts.Token);
        var cookies = JsonSerializer.Deserialize<Dictionary<string, string>>(json)!;
        Assert.Equal("val", cookies["site"]);
    }

    [Fact(DisplayName = "Cookie-TLS-007: Path-scoped cookie sent only for matching path over HTTPS")]
    public async Task Path_Scoped_Cookie_Sent_For_Matching_Path_Over_Https()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = CreateCookieClient();

        var setRequest = new HttpRequestMessage(HttpMethod.Get, "/cookie/set-path/scoped/pathval/cookie");
        var setResponse = await helper.Client.SendAsync(setRequest, cts.Token);
        Assert.Equal(HttpStatusCode.OK, setResponse.StatusCode);

        var echoRequest = new HttpRequestMessage(HttpMethod.Get, "/cookie/echo");
        var echoResponse = await helper.Client.SendAsync(echoRequest, cts.Token);
        Assert.Equal(HttpStatusCode.OK, echoResponse.StatusCode);

        var json = await echoResponse.Content.ReadAsStringAsync(cts.Token);
        var cookies = JsonSerializer.Deserialize<Dictionary<string, string>>(json)!;
        Assert.Equal("pathval", cookies["scoped"]);
    }

    [Fact(DisplayName = "Cookie-TLS-008: Echo returns empty when no cookies set over HTTPS")]
    public async Task Echo_Returns_Empty_When_No_Cookies_Over_Https()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = CreateCookieClient();

        var echoRequest = new HttpRequestMessage(HttpMethod.Get, "/cookie/echo");
        var echoResponse = await helper.Client.SendAsync(echoRequest, cts.Token);
        Assert.Equal(HttpStatusCode.OK, echoResponse.StatusCode);

        var json = await echoResponse.Content.ReadAsStringAsync(cts.Token);
        var cookies = JsonSerializer.Deserialize<Dictionary<string, string>>(json)!;
        Assert.Empty(cookies);
    }

    [Fact(DisplayName = "Cookie-TLS-009: Multiple Set-Cookie headers all stored over HTTPS")]
    public async Task Multiple_SetCookie_Headers_All_Stored_Over_Https()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = CreateCookieClient();

        var setRequest = new HttpRequestMessage(HttpMethod.Get, "/cookie/set-multiple");
        var setResponse = await helper.Client.SendAsync(setRequest, cts.Token);
        Assert.Equal(HttpStatusCode.OK, setResponse.StatusCode);

        var echoRequest = new HttpRequestMessage(HttpMethod.Get, "/cookie/echo");
        var echoResponse = await helper.Client.SendAsync(echoRequest, cts.Token);
        Assert.Equal(HttpStatusCode.OK, echoResponse.StatusCode);

        var json = await echoResponse.Content.ReadAsStringAsync(cts.Token);
        var cookies = JsonSerializer.Deserialize<Dictionary<string, string>>(json)!;
        Assert.Equal("one", cookies["alpha"]);
        Assert.Equal("two", cookies["beta"]);
        Assert.Equal("three", cookies["gamma"]);
    }

    [Fact(DisplayName = "Cookie-TLS-010: Delete cookie via Max-Age=0 over HTTPS")]
    public async Task Delete_Cookie_Via_MaxAge_Zero_Over_Https()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = CreateCookieClient();

        var setRequest = new HttpRequestMessage(HttpMethod.Get, "/cookie/set/victim/alive");
        var setResponse = await helper.Client.SendAsync(setRequest, cts.Token);
        Assert.Equal(HttpStatusCode.OK, setResponse.StatusCode);

        var echoRequest1 = new HttpRequestMessage(HttpMethod.Get, "/cookie/echo");
        var echoResponse1 = await helper.Client.SendAsync(echoRequest1, cts.Token);
        var json1 = await echoResponse1.Content.ReadAsStringAsync(cts.Token);
        var cookies1 = JsonSerializer.Deserialize<Dictionary<string, string>>(json1)!;
        Assert.Equal("alive", cookies1["victim"]);

        var deleteRequest = new HttpRequestMessage(HttpMethod.Get, "/cookie/delete/victim");
        var deleteResponse = await helper.Client.SendAsync(deleteRequest, cts.Token);
        Assert.Equal(HttpStatusCode.OK, deleteResponse.StatusCode);

        var echoRequest2 = new HttpRequestMessage(HttpMethod.Get, "/cookie/echo");
        var echoResponse2 = await helper.Client.SendAsync(echoRequest2, cts.Token);
        var json2 = await echoResponse2.Content.ReadAsStringAsync(cts.Token);
        var cookies2 = JsonSerializer.Deserialize<Dictionary<string, string>>(json2)!;
        Assert.False(cookies2.ContainsKey("victim"), "Deleted cookie should not be sent");
    }

    [Fact(DisplayName = "Cookie-TLS-011: Set cookie persists across redirect response over HTTPS")]
    public async Task Set_Cookie_Persists_Across_Redirect_Over_Https()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = CreateCookieClient();

        var setRequest = new HttpRequestMessage(HttpMethod.Get, "/cookie/set-and-redirect");
        var setResponse = await helper.Client.SendAsync(setRequest, cts.Token);
        Assert.Equal(HttpStatusCode.Found, setResponse.StatusCode);

        var echoRequest = new HttpRequestMessage(HttpMethod.Get, "/cookie/echo");
        var echoResponse = await helper.Client.SendAsync(echoRequest, cts.Token);
        Assert.Equal(HttpStatusCode.OK, echoResponse.StatusCode);

        var json = await echoResponse.Content.ReadAsStringAsync(cts.Token);
        var cookies = JsonSerializer.Deserialize<Dictionary<string, string>>(json)!;
        Assert.Equal("from-redirect", cookies["redirect_cookie"]);
    }
}
