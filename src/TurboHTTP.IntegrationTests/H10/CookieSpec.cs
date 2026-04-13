using System.Net;
using System.Text.Json;
using TurboHTTP.IntegrationTests.Shared;
using TurboHTTP.Protocol.Cookies;

namespace TurboHTTP.IntegrationTests.H10;

[Collection("H10")]
public sealed class CookieSpec
{
    private readonly ServerFixture _server;
    private readonly ActorSystemFixture _systemFixture;

    public CookieSpec(ServerFixture server, ActorSystemFixture systemFixture)
    {
        _server = server;
        _systemFixture = systemFixture;
    }

    private ClientHelper CreateCookieClient(CookieJar jar)
    {
        return ClientHelper.CreateClient(
            _server.H1Port,
            new Version(1, 0),
            configure: builder => builder.WithCookies(jar),
            system: _systemFixture.System);
    }

    [Fact(Timeout = 30000)]
    public async Task Cookie_should_roundtrip_set_and_echo()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var jar = new CookieJar();

        await using var helper = CreateCookieClient(jar);

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

    [Fact(Timeout = 30000)]
    public async Task Cookie_must_not_be_sent_over_plaintext_when_secure()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var jar = new CookieJar();

        await using var helper = CreateCookieClient(jar);

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

    [Fact(Timeout = 30000)]
    public async Task Cookie_should_send_httponly_on_subsequent_requests()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var jar = new CookieJar();

        await using var helper = CreateCookieClient(jar);

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

    [Theory(Timeout = 30000)]
    [InlineData("Strict")]
    [InlineData("Lax")]
    [InlineData("None")]
    public async Task Cookie_should_store_and_send_samesite(string policy)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var jar = new CookieJar();

        await using var helper = CreateCookieClient(jar);

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

    [Fact(Timeout = 45000)]
    public async Task Cookie_should_not_be_sent_after_max_age_expires()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        var jar = new CookieJar();

        await using var helper = CreateCookieClient(jar);

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

    [Fact(Timeout = 30000)]
    public async Task Cookie_should_be_stored_when_domain_scoped()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var jar = new CookieJar();

        await using var helper = CreateCookieClient(jar);

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

    [Fact(Timeout = 30000)]
    public async Task Cookie_should_be_sent_for_matching_path_when_path_scoped()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var jar = new CookieJar();

        await using var helper = CreateCookieClient(jar);

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

    [Fact(Timeout = 30000)]
    public async Task Cookie_should_return_empty_when_no_cookies_set()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var jar = new CookieJar();

        await using var helper = CreateCookieClient(jar);

        var echoRequest = new HttpRequestMessage(HttpMethod.Get, "/cookie/echo");
        var echoResponse = await helper.Client.SendAsync(echoRequest, cts.Token);
        Assert.Equal(HttpStatusCode.OK, echoResponse.StatusCode);

        var json = await echoResponse.Content.ReadAsStringAsync(cts.Token);
        var cookies = JsonSerializer.Deserialize<Dictionary<string, string>>(json)!;
        Assert.Empty(cookies);
    }

    [Fact(Timeout = 30000)]
    public async Task Cookie_should_store_all_multiple_setcookie_headers()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var jar = new CookieJar();

        await using var helper = CreateCookieClient(jar);

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

    [Fact(Timeout = 45000)]
    public async Task Cookie_should_be_deleted_via_max_age_zero()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        var jar = new CookieJar();

        await using var helper = CreateCookieClient(jar);

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

    [Fact(Timeout = 30000)]
    public async Task Cookie_should_persist_across_redirect_response()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var jar = new CookieJar();

        await using var helper = CreateCookieClient(jar);

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
