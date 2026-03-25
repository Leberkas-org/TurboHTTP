using System.Net;
using System.Text.Json;
using TurboHttp.IntegrationTests.Shared;

namespace TurboHttp.IntegrationTests.H2;

[Collection("H2")]
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
            _server.H2Port,
            new Version(2, 0),
            configure: builder => builder.WithCookies(),
            system: _systemFixture.System);
    }

    [Fact(DisplayName = "Cookie-H2-001: Set cookie and echo roundtrip")]
    public async Task Set_Cookie_And_Echo_Roundtrip()
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

    [Fact(DisplayName = "Cookie-H2-002: Secure cookie not sent over plaintext HTTP")]
    public async Task Secure_Cookie_Not_Sent_Over_Plaintext()
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
        Assert.False(cookies.ContainsKey("secret"), "Secure cookie should not be sent over plaintext HTTP");
    }

    [Fact(DisplayName = "Cookie-H2-003: HttpOnly cookie is sent on subsequent requests")]
    public async Task HttpOnly_Cookie_Sent_On_Subsequent_Requests()
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

    [Theory(DisplayName = "Cookie-H2-004: SameSite cookie with policy is stored and sent")]
    [InlineData("Strict")]
    [InlineData("Lax")]
    [InlineData("None")]
    public async Task SameSite_Cookie_Stored_And_Sent(string policy)
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

    [Fact(DisplayName = "Cookie-H2-005: Expired cookie not sent after Max-Age elapses")]
    public async Task Expired_Cookie_Not_Sent()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = CreateCookieClient();

        // Set cookie with Max-Age=1 (expires in 1 second)
        var setRequest = new HttpRequestMessage(HttpMethod.Get, "/cookie/set-expires/temp/value/1");
        var setResponse = await helper.Client.SendAsync(setRequest, cts.Token);
        Assert.Equal(HttpStatusCode.OK, setResponse.StatusCode);

        // Verify cookie is present immediately
        var echoRequest1 = new HttpRequestMessage(HttpMethod.Get, "/cookie/echo");
        var echoResponse1 = await helper.Client.SendAsync(echoRequest1, cts.Token);
        var json1 = await echoResponse1.Content.ReadAsStringAsync(cts.Token);
        var cookies1 = JsonSerializer.Deserialize<Dictionary<string, string>>(json1)!;
        Assert.Equal("value", cookies1["temp"]);

        // Wait for expiry
        await Task.Delay(TimeSpan.FromSeconds(2), cts.Token);

        // Verify cookie is gone
        var echoRequest2 = new HttpRequestMessage(HttpMethod.Get, "/cookie/echo");
        var echoResponse2 = await helper.Client.SendAsync(echoRequest2, cts.Token);
        var json2 = await echoResponse2.Content.ReadAsStringAsync(cts.Token);
        var cookies2 = JsonSerializer.Deserialize<Dictionary<string, string>>(json2)!;
        Assert.False(cookies2.ContainsKey("temp"), "Expired cookie should not be sent");
    }

    [Fact(DisplayName = "Cookie-H2-006: Domain-scoped cookie is stored")]
    public async Task Domain_Scoped_Cookie_Stored()
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

    [Fact(DisplayName = "Cookie-H2-007: Path-scoped cookie sent only for matching path")]
    public async Task Path_Scoped_Cookie_Sent_For_Matching_Path()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = CreateCookieClient();

        // Set cookie scoped to /cookie path
        var setRequest = new HttpRequestMessage(HttpMethod.Get, "/cookie/set-path/scoped/pathval/cookie");
        var setResponse = await helper.Client.SendAsync(setRequest, cts.Token);
        Assert.Equal(HttpStatusCode.OK, setResponse.StatusCode);

        // Echo is under /cookie — should include the cookie
        var echoRequest = new HttpRequestMessage(HttpMethod.Get, "/cookie/echo");
        var echoResponse = await helper.Client.SendAsync(echoRequest, cts.Token);
        Assert.Equal(HttpStatusCode.OK, echoResponse.StatusCode);

        var json = await echoResponse.Content.ReadAsStringAsync(cts.Token);
        var cookies = JsonSerializer.Deserialize<Dictionary<string, string>>(json)!;
        Assert.Equal("pathval", cookies["scoped"]);
    }

    [Fact(DisplayName = "Cookie-H2-008: Echo returns empty when no cookies set")]
    public async Task Echo_Returns_Empty_When_No_Cookies()
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

    [Fact(DisplayName = "Cookie-H2-009: Multiple Set-Cookie headers all stored")]
    public async Task Multiple_SetCookie_Headers_All_Stored()
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

    [Fact(DisplayName = "Cookie-H2-010: Delete cookie via Max-Age=0")]
    public async Task Delete_Cookie_Via_MaxAge_Zero()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = CreateCookieClient();

        // Set a cookie first
        var setRequest = new HttpRequestMessage(HttpMethod.Get, "/cookie/set/victim/alive");
        var setResponse = await helper.Client.SendAsync(setRequest, cts.Token);
        Assert.Equal(HttpStatusCode.OK, setResponse.StatusCode);

        // Verify it exists
        var echoRequest1 = new HttpRequestMessage(HttpMethod.Get, "/cookie/echo");
        var echoResponse1 = await helper.Client.SendAsync(echoRequest1, cts.Token);
        var json1 = await echoResponse1.Content.ReadAsStringAsync(cts.Token);
        var cookies1 = JsonSerializer.Deserialize<Dictionary<string, string>>(json1)!;
        Assert.Equal("alive", cookies1["victim"]);

        // Delete it
        var deleteRequest = new HttpRequestMessage(HttpMethod.Get, "/cookie/delete/victim");
        var deleteResponse = await helper.Client.SendAsync(deleteRequest, cts.Token);
        Assert.Equal(HttpStatusCode.OK, deleteResponse.StatusCode);

        // Verify it is gone
        var echoRequest2 = new HttpRequestMessage(HttpMethod.Get, "/cookie/echo");
        var echoResponse2 = await helper.Client.SendAsync(echoRequest2, cts.Token);
        var json2 = await echoResponse2.Content.ReadAsStringAsync(cts.Token);
        var cookies2 = JsonSerializer.Deserialize<Dictionary<string, string>>(json2)!;
        Assert.False(cookies2.ContainsKey("victim"), "Deleted cookie should not be sent");
    }

    [Fact(DisplayName = "Cookie-H2-011: Set cookie persists across redirect response")]
    public async Task Set_Cookie_Persists_Across_Redirect()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = CreateCookieClient();

        // This route sets a cookie and returns a 302 redirect to /cookie/echo.
        // Without automatic redirect following, we get the 302 back —
        // but the CookieJar should still store the Set-Cookie from the response.
        var setRequest = new HttpRequestMessage(HttpMethod.Get, "/cookie/set-and-redirect");
        var setResponse = await helper.Client.SendAsync(setRequest, cts.Token);
        Assert.Equal(HttpStatusCode.Found, setResponse.StatusCode);

        // Manually follow the redirect — the cookie should be sent along
        var echoRequest = new HttpRequestMessage(HttpMethod.Get, "/cookie/echo");
        var echoResponse = await helper.Client.SendAsync(echoRequest, cts.Token);
        Assert.Equal(HttpStatusCode.OK, echoResponse.StatusCode);

        var json = await echoResponse.Content.ReadAsStringAsync(cts.Token);
        var cookies = JsonSerializer.Deserialize<Dictionary<string, string>>(json)!;
        Assert.Equal("from-redirect", cookies["redirect_cookie"]);
    }
}
