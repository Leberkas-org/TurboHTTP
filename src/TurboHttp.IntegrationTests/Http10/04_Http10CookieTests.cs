using System.Net;
using System.Text.Json;
using Akka.Actor;
using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHttp.IntegrationTests.Shared;
using TurboHttp.IO;
using TurboHttp.Protocol;
using TurboHttp.Streams;
using TurboHttp.Streams.Stages;

namespace TurboHttp.IntegrationTests.Http10;

/// <summary>
/// Integration tests for Http10Engine cookie handling.
/// Verifies RFC 6265 cookie semantics (Set-Cookie storage, accumulation,
/// Path restriction, deletion, expiry, cross-redirect persistence)
/// against real Kestrel cookie routes via CookieJar + Http10Engine.
/// </summary>
public sealed class Http10CookieTests : TestKit, IClassFixture<KestrelFixture>
{
    private readonly KestrelFixture _fixture;
    private readonly IMaterializer _materializer;
    private readonly IActorRef _clientManager;

    public Http10CookieTests(KestrelFixture fixture)
    {
        _fixture = fixture;
        _materializer = Sys.Materializer();
        _clientManager = Sys.ActorOf(Props.Create<ClientManager>());
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request)
    {
        var engine = new Http10Engine();
        var tcpOptions = new TcpOptions
        {
            Host = "127.0.0.1",
            Port = _fixture.Port
        };

        var transport =
            Flow.Create<ITransportItem>()
                .Prepend(Source.Single<ITransportItem>(
                    new ConnectItem(tcpOptions)))
                .Via(new ConnectionStage(_clientManager));

        var flow = engine.CreateFlow().Join(transport);

        var (queue, responseTask) = Source.Queue<HttpRequestMessage>(1, OverflowStrategy.Backpressure)
            .Via(flow)
            .ToMaterialized(Sink.First<HttpResponseMessage>(), Keep.Both)
            .Run(_materializer);

        await queue.OfferAsync(request);
        return await responseTask.WaitAsync(TimeSpan.FromSeconds(10));
    }

    /// <summary>
    /// Sends a request with CookieJar integration. Processes Set-Cookie from
    /// responses and adds stored cookies to subsequent requests.
    /// Follows redirects manually (HTTP/1.0 = new connection per request).
    /// </summary>
    private async Task<HttpResponseMessage> SendWithCookiesAsync(
        HttpRequestMessage request,
        CookieJar jar,
        bool followRedirects = false)
    {
        var handler = new RedirectHandler();
        var current = request;

        for (var i = 0; i <= (followRedirects ? RedirectPolicy.Default.MaxRedirects : 0); i++)
        {
            var uri = current.RequestUri!;
            jar.AddCookiesToRequest(uri, ref current);

            var response = await SendAsync(current);
            response.RequestMessage = current;

            jar.ProcessResponse(uri, response);

            if (!followRedirects || !RedirectHandler.IsRedirect(response))
            {
                return response;
            }

            current = handler.BuildRedirectRequest(current, response);
            current.Version = HttpVersion.Version10;
        }

        throw new RedirectException(
            "Exceeded redirect loop in test helper",
            RedirectError.MaxRedirectsExceeded);
    }

    private HttpRequestMessage CreateGet(string path) =>
        new(HttpMethod.Get, $"http://127.0.0.1:{_fixture.Port}{path}")
        {
            Version = HttpVersion.Version10
        };

    private static Dictionary<string, string> ParseCookieEcho(string json) =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? [];

    [Fact(DisplayName = "COOKIE-INT-001: Set-Cookie stored and sent back on next request")]
    public async Task SetCookie_StoredAndSentBack()
    {
        var jar = new CookieJar();

        // Step 1: Set a cookie
        var setResponse = await SendWithCookiesAsync(CreateGet("/cookie/set/session/abc123"), jar);
        Assert.Equal(HttpStatusCode.OK, setResponse.StatusCode);

        // Step 2: Echo cookies back — should include session=abc123
        var echoResponse = await SendWithCookiesAsync(CreateGet("/cookie/echo"), jar);
        var body = await echoResponse.Content.ReadAsStringAsync();
        var cookies = ParseCookieEcho(body);

        Assert.True(cookies.ContainsKey("session"), "Cookie 'session' should be sent back");
        Assert.Equal("abc123", cookies["session"]);
    }

    [Fact(DisplayName = "COOKIE-INT-002: Multiple Set-Cookie headers stored and accumulated")]
    public async Task MultipleCookies_StoredAndAccumulated()
    {
        var jar = new CookieJar();

        // Step 1: Set multiple cookies in one response
        var setResponse = await SendWithCookiesAsync(CreateGet("/cookie/set-multiple"), jar);
        Assert.Equal(HttpStatusCode.OK, setResponse.StatusCode);

        // Step 2: Echo all cookies back
        var echoResponse = await SendWithCookiesAsync(CreateGet("/cookie/echo"), jar);
        var body = await echoResponse.Content.ReadAsStringAsync();
        var cookies = ParseCookieEcho(body);

        Assert.Equal("one", cookies["alpha"]);
        Assert.Equal("two", cookies["beta"]);
        Assert.Equal("three", cookies["gamma"]);
    }

    [Fact(DisplayName = "COOKIE-INT-003: Path attribute restricts cookie to matching path")]
    public async Task PathAttribute_RestrictsCookieScope()
    {
        var jar = new CookieJar();

        // Set cookie with Path=/cookie/restricted
        var setResponse = await SendWithCookiesAsync(
            CreateGet("/cookie/set-path/pathcookie/pval/cookie/restricted"), jar);
        Assert.Equal(HttpStatusCode.OK, setResponse.StatusCode);

        // /cookie/echo is NOT under /cookie/restricted — cookie should NOT be sent
        var echoResponse = await SendWithCookiesAsync(CreateGet("/cookie/echo"), jar);
        var body = await echoResponse.Content.ReadAsStringAsync();
        var cookies = ParseCookieEcho(body);

        Assert.False(cookies.ContainsKey("pathcookie"),
            "Cookie with Path=/cookie/restricted should not be sent to /cookie/echo");
    }

    [Fact(DisplayName = "COOKIE-INT-004: Max-Age=0 deletes a previously stored cookie")]
    public async Task MaxAgeZero_DeletesCookie()
    {
        var jar = new CookieJar();

        // Step 1: Set cookie
        var setResponse = await SendWithCookiesAsync(CreateGet("/cookie/set/toDelete/val"), jar);
        Assert.Equal(HttpStatusCode.OK, setResponse.StatusCode);

        // Step 2: Delete cookie via Max-Age=0
        var deleteResponse = await SendWithCookiesAsync(CreateGet("/cookie/delete/toDelete"), jar);
        Assert.Equal(HttpStatusCode.OK, deleteResponse.StatusCode);

        // Step 3: Echo — cookie should be gone
        var echoResponse = await SendWithCookiesAsync(CreateGet("/cookie/echo"), jar);
        var body = await echoResponse.Content.ReadAsStringAsync();
        var cookies = ParseCookieEcho(body);

        Assert.False(cookies.ContainsKey("toDelete"),
            "Cookie 'toDelete' should have been removed by Max-Age=0");
    }

    [Fact(DisplayName = "COOKIE-INT-005: Expired cookie (Max-Age=0 at set time) is not sent")]
    public async Task ExpiredCookie_NotSent()
    {
        var jar = new CookieJar();

        // Set a cookie that expires immediately (Max-Age=0)
        var setResponse = await SendWithCookiesAsync(
            CreateGet("/cookie/set-expires/expiring/val/0"), jar);
        Assert.Equal(HttpStatusCode.OK, setResponse.StatusCode);

        // Echo — the cookie was already expired at set time, should not appear
        var echoResponse = await SendWithCookiesAsync(CreateGet("/cookie/echo"), jar);
        var body = await echoResponse.Content.ReadAsStringAsync();
        var cookies = ParseCookieEcho(body);

        Assert.False(cookies.ContainsKey("expiring"),
            "Cookie with Max-Age=0 should not be stored or sent");
    }

    [Fact(DisplayName = "COOKIE-INT-006: Cookies persist across 302 redirect")]
    public async Task Cookies_PersistAcrossRedirect()
    {
        var jar = new CookieJar();

        // /cookie/set-and-redirect sets a cookie then 302 → /cookie/echo
        // The redirect handler should carry the cookie jar through
        var response = await SendWithCookiesAsync(
            CreateGet("/cookie/set-and-redirect"), jar, followRedirects: true);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        var cookies = ParseCookieEcho(body);

        Assert.True(cookies.ContainsKey("redirect_cookie"),
            "Cookie set during redirect should persist to final destination");
        Assert.Equal("from-redirect", cookies["redirect_cookie"]);
    }
}
