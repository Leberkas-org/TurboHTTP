using System.Net;
using Akka.Actor;
using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHttp.IntegrationTests.Shared;
using TurboHttp.IO;
using TurboHttp.Protocol;
using TurboHttp.Streams;
using TurboHttp.Streams.Stages;

namespace TurboHttp.IntegrationTests.Http11;

/// <summary>
/// Integration tests for Http11Engine redirect handling.
/// Verifies RFC 9110 §15.4 redirect semantics (301/302/307/308 follow, method rewriting,
/// chains, loop detection, relative Location, cross-origin header stripping,
/// HTTPS→HTTP downgrade protection, and cookie preservation across redirects)
/// against real Kestrel redirect routes.
/// </summary>
public sealed class Http11RedirectTests : TestKit, IClassFixture<KestrelFixture>
{
    private readonly KestrelFixture _fixture;
    private readonly IMaterializer _materializer;
    private readonly IActorRef _clientManager;

    public Http11RedirectTests(KestrelFixture fixture)
    {
        _fixture = fixture;
        _materializer = Sys.Materializer();
        _clientManager = Sys.ActorOf(Props.Create<ClientManager>());
    }

    /// <summary>
    /// Sends a single HTTP/1.1 request through the Http11Engine pipeline.
    /// Each call materialises a fresh pipeline.
    /// </summary>
    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, string host = "127.0.0.1", int? port = null)
    {
        var engine = new Http11Engine();
        var tcpOptions = new TcpOptions
        {
            Host = host,
            Port = port ?? _fixture.Port
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
    /// Sends a request and manually follows redirects using <see cref="RedirectHandler"/>.
    /// Each redirect materialises a new Http11Engine pipeline.
    /// </summary>
    /// <param name="bodyBytes">
    /// Optional body bytes for body-preserving redirects (307/308).
    /// The encoder disposes the content stream, so we must create fresh content for each hop.
    /// </param>
    private async Task<HttpResponseMessage> SendWithRedirectAsync(
        HttpRequestMessage request,
        RedirectHandler? handler = null,
        CookieJar? cookieJar = null,
        byte[]? bodyBytes = null,
        string? bodyContentType = null)
    {
        handler ??= new RedirectHandler();
        var current = request;

        for (var i = 0; i <= RedirectPolicy.Default.MaxRedirects; i++)
        {
            // Inject cookies if a CookieJar is provided
            if (cookieJar is not null && current.RequestUri is not null)
            {
                cookieJar.AddCookiesToRequest(current.RequestUri, ref current);
            }

            var host = current.RequestUri?.Host ?? "127.0.0.1";
            var port = current.RequestUri?.Port ?? _fixture.Port;
            var response = await SendAsync(current, host, port);
            response.RequestMessage = current;

            // Store cookies from response if a CookieJar is provided
            if (cookieJar is not null && current.RequestUri is not null)
            {
                cookieJar.ProcessResponse(current.RequestUri, response);
            }

            if (!RedirectHandler.IsRedirect(response))
            {
                return response;
            }

            current = handler.BuildRedirectRequest(current, response);
            current.Version = HttpVersion.Version11;

            // For body-preserving redirects (307/308), replace with fresh content
            // because the encoder disposes the content stream after each send
            if (bodyBytes is not null && current.Content is not null)
            {
                current.Content = new ByteArrayContent(bodyBytes);
                if (bodyContentType is not null)
                {
                    current.Content.Headers.ContentType =
                        System.Net.Http.Headers.MediaTypeHeaderValue.Parse(bodyContentType);
                }
            }
        }

        throw new RedirectException(
            "Exceeded redirect loop in test helper",
            RedirectError.MaxRedirectsExceeded);
    }

    [Theory(DisplayName = "REDIR-11-001: GET 301/302/307/308 redirect follows Location to /hello")]
    [InlineData(301)]
    [InlineData(302)]
    [InlineData(307)]
    [InlineData(308)]
    public async Task Get_301_302_307_308_FollowsRedirect(int statusCode)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"http://127.0.0.1:{_fixture.Port}/redirect/{statusCode}/hello")
        {
            Version = HttpVersion.Version11
        };

        var response = await SendWithRedirectAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("Hello World", body);
    }

    [Fact(DisplayName = "REDIR-11-002: POST 303 rewrites method to GET (RFC 9110 §15.4.4)")]
    public async Task Post_303_RewritesToGet()
    {
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"http://127.0.0.1:{_fixture.Port}/redirect/303")
        {
            Version = HttpVersion.Version11,
            Content = new StringContent("should-be-dropped")
        };

        var response = await SendWithRedirectAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        // 303 rewrites POST → GET, redirects to /hello which returns "Hello World"
        Assert.Equal("Hello World", body);
    }

    [Fact(DisplayName = "REDIR-11-003: POST 307 preserves method and body (RFC 9110 §15.4.8)")]
    public async Task Post_307_PreservesMethodAndBody()
    {
        var bodyBytes = System.Text.Encoding.UTF8.GetBytes("preserved-body");
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"http://127.0.0.1:{_fixture.Port}/redirect/307")
        {
            Version = HttpVersion.Version11,
            Content = new ByteArrayContent(bodyBytes)
        };
        request.Content.Headers.ContentType =
            System.Net.Http.Headers.MediaTypeHeaderValue.Parse("text/plain; charset=utf-8");

        var response = await SendWithRedirectAsync(request,
            bodyBytes: bodyBytes, bodyContentType: "text/plain; charset=utf-8");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        // 307 preserves POST → POST, redirects to /echo which echoes the body
        Assert.Equal("preserved-body", body);
    }

    [Fact(DisplayName = "REDIR-11-004: POST 308 preserves method and body (RFC 9110 §15.4.9)")]
    public async Task Post_308_PreservesMethodAndBody()
    {
        var bodyBytes = System.Text.Encoding.UTF8.GetBytes("preserved-body");
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"http://127.0.0.1:{_fixture.Port}/redirect/308")
        {
            Version = HttpVersion.Version11,
            Content = new ByteArrayContent(bodyBytes)
        };
        request.Content.Headers.ContentType =
            System.Net.Http.Headers.MediaTypeHeaderValue.Parse("text/plain; charset=utf-8");

        var response = await SendWithRedirectAsync(request,
            bodyBytes: bodyBytes, bodyContentType: "text/plain; charset=utf-8");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        // 308 preserves POST → POST, redirects to /echo which echoes the body
        Assert.Equal("preserved-body", body);
    }

    [Fact(DisplayName = "REDIR-11-005: Redirect chain (5 hops) follows to final /hello")]
    public async Task RedirectChain_5Hops_FollowsToEnd()
    {
        var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"http://127.0.0.1:{_fixture.Port}/redirect/chain/5")
        {
            Version = HttpVersion.Version11
        };

        var response = await SendWithRedirectAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("Hello World", body);
    }

    [Fact(DisplayName = "REDIR-11-006: Redirect loop detected — throws RedirectException")]
    public async Task RedirectLoop_Detected()
    {
        var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"http://127.0.0.1:{_fixture.Port}/redirect/loop")
        {
            Version = HttpVersion.Version11
        };

        var ex = await Assert.ThrowsAsync<RedirectException>(
            () => SendWithRedirectAsync(request));

        Assert.Equal(RedirectError.RedirectLoop, ex.Error);
    }

    [Fact(DisplayName = "REDIR-11-007: Cross-origin redirect strips Authorization header")]
    public async Task CrossOrigin_StripsAuthorization()
    {
        // Connect via localhost, redirect goes to 127.0.0.1 (different origin)
        // The redirect target is /auth which returns 401 when Authorization is missing
        var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"http://localhost:{_fixture.Port}/redirect/cross-origin-auth")
        {
            Version = HttpVersion.Version11
        };
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer secret-token");

        var response = await SendWithRedirectAsync(request);

        // After cross-origin redirect, Authorization should be stripped → /auth returns 401
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact(DisplayName = "REDIR-11-008: HTTPS→HTTP downgrade blocked — throws RedirectDowngradeException")]
    public async Task HttpsToHttp_Downgrade_Blocked()
    {
        // Fetch a real redirect response from /redirect/cross-scheme (returns Location: http://...)
        // Then simulate the HTTPS→HTTP downgrade by presenting it to RedirectHandler
        // as if the original request was HTTPS
        var httpRequest = new HttpRequestMessage(
            HttpMethod.Get,
            $"http://127.0.0.1:{_fixture.Port}/redirect/cross-scheme")
        {
            Version = HttpVersion.Version11
        };

        var response = await SendAsync(httpRequest);
        Assert.True(RedirectHandler.IsRedirect(response));

        // Build a synthetic HTTPS original request so RedirectHandler detects the downgrade
        var httpsRequest = new HttpRequestMessage(
            HttpMethod.Get,
            $"https://127.0.0.1:{_fixture.Port}/redirect/cross-scheme")
        {
            Version = HttpVersion.Version11
        };
        response.RequestMessage = httpsRequest;

        var handler = new RedirectHandler();
        Assert.Throws<RedirectDowngradeException>(
            () => handler.BuildRedirectRequest(httpsRequest, response));
    }

    [Fact(DisplayName = "REDIR-11-009: Relative Location header resolved correctly")]
    public async Task RelativeLocation_ResolvedCorrectly()
    {
        // /redirect/relative returns Location: hello (relative)
        // Resolved against http://127.0.0.1:{port}/redirect/relative → /redirect/hello
        var handler = new RedirectHandler();
        var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"http://127.0.0.1:{_fixture.Port}/redirect/relative")
        {
            Version = HttpVersion.Version11
        };

        var response = await SendWithRedirectAsync(request, handler);

        // The redirect was followed — response is not a 3xx
        Assert.False(RedirectHandler.IsRedirect(response),
            "Redirect should have been followed");
        // The resolved URI should be the relative "hello" resolved against the original path
        Assert.EndsWith("/redirect/hello", response.RequestMessage?.RequestUri?.AbsolutePath ?? "");
    }

    [Fact(DisplayName = "REDIR-11-010: Cookies preserved across redirects via CookieJar")]
    public async Task Cookies_PreservedAcrossRedirects()
    {
        // /cookie/set-and-redirect sets a cookie and redirects to /cookie/echo
        // /cookie/echo returns received cookies as JSON body
        var cookieJar = new CookieJar();
        var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"http://127.0.0.1:{_fixture.Port}/cookie/set-and-redirect")
        {
            Version = HttpVersion.Version11
        };

        var response = await SendWithRedirectAsync(request, cookieJar: cookieJar);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        // The cookie "redirect_cookie=from-redirect" should have been sent to /cookie/echo
        Assert.Contains("redirect_cookie", body);
        Assert.Contains("from-redirect", body);
    }
}
