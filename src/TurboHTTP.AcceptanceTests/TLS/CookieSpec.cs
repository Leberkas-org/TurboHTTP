using System.Net;
using System.Text.Json;
using Akka.Streams.Dsl;
using TurboHTTP.Protocol.Cookies;
using TurboHTTP.Streams.Stages.Features;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.AcceptanceTests.TLS;

public sealed class CookieSpec : AcceptanceTestBase
{
    private static HttpResponseMessage EchoCookies(HttpRequestMessage req)
    {
        var cookies = new Dictionary<string, string>();
        if (req.Headers.TryGetValues("Cookie", out var values))
        {
            foreach (var v in values)
            {
                foreach (var pair in v.Split(';', StringSplitOptions.TrimEntries))
                {
                    var eq = pair.IndexOf('=');
                    if (eq > 0)
                    {
                        cookies[pair[..eq].Trim()] = pair[(eq + 1)..].Trim();
                    }
                }
            }
        }
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(cookies))
        };
    }

    private static HttpResponseMessage SetCookieResponse(string setCookie)
    {
        var r = new HttpResponseMessage(HttpStatusCode.OK);
        r.Headers.TryAddWithoutValidation("Set-Cookie", setCookie);
        return r;
    }

    private async Task<HttpResponseMessage> SendAsync(ResponseMap map, HttpRequestMessage request, CookieJar jar)
    {
        var cookie = BidiFlow.FromGraph(new CookieBidiStage(jar));
        var fake = ResponseMapFake.Create(map);
        var flow = cookie.Atop(fake)
            .Join(Flow.FromFunction<HttpRequestMessage, HttpResponseMessage>(_ => new HttpResponseMessage()));

        var tcs = new TaskCompletionSource<HttpResponseMessage>();
        _ = Source.Single(request)
            .Via(flow)
            .RunWith(Sink.ForEach<HttpResponseMessage>(res => tcs.TrySetResult(res)), Materializer);

        return await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC6265-5.4")]
    public async Task Cookie_should_set_and_echo_roundtrip_over_https()
    {
        var jar = new CookieJar();
        var map = new ResponseMap()
            .On("/cookie/set/session/abc123", _ => SetCookieResponse("session=abc123; Path=/"))
            .On("/cookie/echo", EchoCookies);

        var setResponse = await SendAsync(map,
            new HttpRequestMessage(HttpMethod.Get, "https://localhost/cookie/set/session/abc123"), jar);
        Assert.Equal(HttpStatusCode.OK, setResponse.StatusCode);

        var echoResponse = await SendAsync(map,
            new HttpRequestMessage(HttpMethod.Get, "https://localhost/cookie/echo"), jar);
        Assert.Equal(HttpStatusCode.OK, echoResponse.StatusCode);

        var json = await echoResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var cookies = JsonSerializer.Deserialize<Dictionary<string, string>>(json)!;
        Assert.Equal("abc123", cookies["session"]);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC6265-5.4")]
    public async Task Secure_cookie_should_be_sent_over_https()
    {
        var jar = new CookieJar();
        var map = new ResponseMap()
            .On("/cookie/set-secure/secret/hidden", _ => SetCookieResponse("secret=hidden; Path=/; Secure"))
            .On("/cookie/echo", EchoCookies);

        await SendAsync(map,
            new HttpRequestMessage(HttpMethod.Get, "https://localhost/cookie/set-secure/secret/hidden"), jar);

        var echoResponse = await SendAsync(map,
            new HttpRequestMessage(HttpMethod.Get, "https://localhost/cookie/echo"), jar);
        Assert.Equal(HttpStatusCode.OK, echoResponse.StatusCode);

        var json = await echoResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var cookies = JsonSerializer.Deserialize<Dictionary<string, string>>(json)!;
        Assert.True(cookies.ContainsKey("secret"), "Secure cookie MUST be sent over HTTPS");
        Assert.Equal("hidden", cookies["secret"]);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC6265-5.3")]
    public async Task HttpOnly_cookie_should_be_sent_on_subsequent_requests_over_https()
    {
        var jar = new CookieJar();
        var map = new ResponseMap()
            .On("/cookie/set-httponly/token/xyz", _ => SetCookieResponse("token=xyz; Path=/; HttpOnly"))
            .On("/cookie/echo", EchoCookies);

        await SendAsync(map,
            new HttpRequestMessage(HttpMethod.Get, "https://localhost/cookie/set-httponly/token/xyz"), jar);

        var echoResponse = await SendAsync(map,
            new HttpRequestMessage(HttpMethod.Get, "https://localhost/cookie/echo"), jar);
        Assert.Equal(HttpStatusCode.OK, echoResponse.StatusCode);

        var json = await echoResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var cookies = JsonSerializer.Deserialize<Dictionary<string, string>>(json)!;
        Assert.Equal("xyz", cookies["token"]);
    }

    [Theory(Timeout = 5000)]
    [InlineData("Strict")]
    [InlineData("Lax")]
    [InlineData("None")]
    [Trait("RFC", "RFC6265-5.3")]
    public async Task SameSite_cookie_should_be_stored_and_sent_over_https(string policy)
    {
        var jar = new CookieJar();
        var map = new ResponseMap()
            .On($"/cookie/set-samesite/pref/{policy}/{policy}",
                _ => SetCookieResponse($"pref={policy}; Path=/; SameSite={policy}"))
            .On("/cookie/echo", EchoCookies);

        await SendAsync(map,
            new HttpRequestMessage(HttpMethod.Get,
                $"https://localhost/cookie/set-samesite/pref/{policy}/{policy}"), jar);

        var echoResponse = await SendAsync(map,
            new HttpRequestMessage(HttpMethod.Get, "https://localhost/cookie/echo"), jar);
        Assert.Equal(HttpStatusCode.OK, echoResponse.StatusCode);

        var json = await echoResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var cookies = JsonSerializer.Deserialize<Dictionary<string, string>>(json)!;
        Assert.Equal(policy, cookies["pref"]);
    }

    [Fact(Timeout = 10000)]
    [Trait("RFC", "RFC6265-5.3")]
    public async Task Expired_cookie_should_not_be_sent_after_max_age_elapses_over_https()
    {
        var jar = new CookieJar();
        var map = new ResponseMap()
            .On("/cookie/set-expires/temp/value/1", _ => SetCookieResponse("temp=value; Path=/; Max-Age=1"))
            .On("/cookie/echo", EchoCookies);

        await SendAsync(map,
            new HttpRequestMessage(HttpMethod.Get, "https://localhost/cookie/set-expires/temp/value/1"), jar);

        var echo1 = await SendAsync(map,
            new HttpRequestMessage(HttpMethod.Get, "https://localhost/cookie/echo"), jar);
        var json1 = await echo1.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var cookies1 = JsonSerializer.Deserialize<Dictionary<string, string>>(json1)!;
        Assert.Equal("value", cookies1["temp"]);

        await Task.Delay(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

        var echo2 = await SendAsync(map,
            new HttpRequestMessage(HttpMethod.Get, "https://localhost/cookie/echo"), jar);
        var json2 = await echo2.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var cookies2 = JsonSerializer.Deserialize<Dictionary<string, string>>(json2)!;
        Assert.False(cookies2.ContainsKey("temp"), "Expired cookie should not be sent");
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC6265-5.3")]
    public async Task Domain_scoped_cookie_should_be_stored_over_https()
    {
        var jar = new CookieJar();
        var map = new ResponseMap()
            .On("/cookie/set-domain/site/val/localhost",
                _ => SetCookieResponse("site=val; Path=/; Domain=localhost"))
            .On("/cookie/echo", EchoCookies);

        await SendAsync(map,
            new HttpRequestMessage(HttpMethod.Get,
                "https://localhost/cookie/set-domain/site/val/localhost"), jar);

        var echoResponse = await SendAsync(map,
            new HttpRequestMessage(HttpMethod.Get, "https://localhost/cookie/echo"), jar);
        Assert.Equal(HttpStatusCode.OK, echoResponse.StatusCode);

        var json = await echoResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var cookies = JsonSerializer.Deserialize<Dictionary<string, string>>(json)!;
        Assert.Equal("val", cookies["site"]);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC6265-5.4")]
    public async Task Path_scoped_cookie_should_be_sent_only_for_matching_path_over_https()
    {
        var jar = new CookieJar();
        var map = new ResponseMap()
            .On("/cookie/set-path/scoped/pathval/cookie",
                _ => SetCookieResponse("scoped=pathval; Path=/cookie"))
            .On("/cookie/echo", EchoCookies);

        await SendAsync(map,
            new HttpRequestMessage(HttpMethod.Get,
                "https://localhost/cookie/set-path/scoped/pathval/cookie"), jar);

        var echoResponse = await SendAsync(map,
            new HttpRequestMessage(HttpMethod.Get, "https://localhost/cookie/echo"), jar);
        Assert.Equal(HttpStatusCode.OK, echoResponse.StatusCode);

        var json = await echoResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var cookies = JsonSerializer.Deserialize<Dictionary<string, string>>(json)!;
        Assert.Equal("pathval", cookies["scoped"]);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC6265-5.4")]
    public async Task Echo_should_return_empty_when_no_cookies_over_https()
    {
        var jar = new CookieJar();
        var map = new ResponseMap()
            .On("/cookie/echo", EchoCookies);

        var echoResponse = await SendAsync(map,
            new HttpRequestMessage(HttpMethod.Get, "https://localhost/cookie/echo"), jar);
        Assert.Equal(HttpStatusCode.OK, echoResponse.StatusCode);

        var json = await echoResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var cookies = JsonSerializer.Deserialize<Dictionary<string, string>>(json)!;
        Assert.Empty(cookies);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC6265-5.3")]
    public async Task Multiple_setcookie_headers_should_all_be_stored_over_https()
    {
        var jar = new CookieJar();
        var map = new ResponseMap()
            .On("/cookie/set-multiple", _ =>
            {
                var r = new HttpResponseMessage(HttpStatusCode.OK);
                r.Headers.TryAddWithoutValidation("Set-Cookie", "alpha=one; Path=/");
                r.Headers.TryAddWithoutValidation("Set-Cookie", "beta=two; Path=/");
                r.Headers.TryAddWithoutValidation("Set-Cookie", "gamma=three; Path=/");
                return r;
            })
            .On("/cookie/echo", EchoCookies);

        await SendAsync(map,
            new HttpRequestMessage(HttpMethod.Get, "https://localhost/cookie/set-multiple"), jar);

        var echoResponse = await SendAsync(map,
            new HttpRequestMessage(HttpMethod.Get, "https://localhost/cookie/echo"), jar);
        Assert.Equal(HttpStatusCode.OK, echoResponse.StatusCode);

        var json = await echoResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var cookies = JsonSerializer.Deserialize<Dictionary<string, string>>(json)!;
        Assert.Equal("one", cookies["alpha"]);
        Assert.Equal("two", cookies["beta"]);
        Assert.Equal("three", cookies["gamma"]);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC6265-5.3")]
    public async Task Delete_cookie_should_work_via_max_age_zero_over_https()
    {
        var jar = new CookieJar();
        var map = new ResponseMap()
            .On("/cookie/set/victim/alive", _ => SetCookieResponse("victim=alive; Path=/"))
            .On("/cookie/delete/victim", _ => SetCookieResponse("victim=; Path=/; Max-Age=0"))
            .On("/cookie/echo", EchoCookies);

        await SendAsync(map,
            new HttpRequestMessage(HttpMethod.Get, "https://localhost/cookie/set/victim/alive"), jar);

        var echo1 = await SendAsync(map,
            new HttpRequestMessage(HttpMethod.Get, "https://localhost/cookie/echo"), jar);
        var json1 = await echo1.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var cookies1 = JsonSerializer.Deserialize<Dictionary<string, string>>(json1)!;
        Assert.Equal("alive", cookies1["victim"]);

        await SendAsync(map,
            new HttpRequestMessage(HttpMethod.Get, "https://localhost/cookie/delete/victim"), jar);

        var echo2 = await SendAsync(map,
            new HttpRequestMessage(HttpMethod.Get, "https://localhost/cookie/echo"), jar);
        var json2 = await echo2.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var cookies2 = JsonSerializer.Deserialize<Dictionary<string, string>>(json2)!;
        Assert.False(cookies2.ContainsKey("victim"), "Deleted cookie should not be sent");
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC6265-5.3")]
    public async Task Set_cookie_should_persist_across_redirect_response_over_https()
    {
        var jar = new CookieJar();
        var map = new ResponseMap()
            .On("/cookie/set-and-redirect", _ =>
            {
                var r = new HttpResponseMessage(HttpStatusCode.Found);
                r.Headers.TryAddWithoutValidation("Set-Cookie", "redirect_cookie=from-redirect; Path=/");
                r.Headers.Location = new Uri("/cookie/echo", UriKind.Relative);
                return r;
            })
            .On("/cookie/echo", EchoCookies);

        var setResponse = await SendAsync(map,
            new HttpRequestMessage(HttpMethod.Get, "https://localhost/cookie/set-and-redirect"), jar);
        Assert.Equal(HttpStatusCode.Found, setResponse.StatusCode);

        var echoResponse = await SendAsync(map,
            new HttpRequestMessage(HttpMethod.Get, "https://localhost/cookie/echo"), jar);
        Assert.Equal(HttpStatusCode.OK, echoResponse.StatusCode);

        var json = await echoResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var cookies = JsonSerializer.Deserialize<Dictionary<string, string>>(json)!;
        Assert.Equal("from-redirect", cookies["redirect_cookie"]);
    }
}
