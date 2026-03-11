using System.Net;
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
/// Integration tests for Http10Engine redirect handling.
/// Verifies RFC 9110 §15.4 redirect semantics (301/302 follow, method rewriting,
/// chains, loop detection, relative Location, cross-origin header stripping)
/// against real Kestrel redirect routes.
/// </summary>
public sealed class Http10RedirectTests : TestKit, IClassFixture<KestrelFixture>
{
    private readonly KestrelFixture _fixture;
    private readonly IMaterializer _materializer;
    private readonly IActorRef _clientManager;

    public Http10RedirectTests(KestrelFixture fixture)
    {
        _fixture = fixture;
        _materializer = Sys.Materializer();
        _clientManager = Sys.ActorOf(Props.Create<ClientManager>());
    }

    /// <summary>
    /// Sends a single HTTP/1.0 request through the Http10Engine pipeline.
    /// Each call materialises a fresh pipeline (HTTP/1.0 closes connection after response).
    /// </summary>
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
    /// Sends a request and manually follows redirects using <see cref="RedirectHandler"/>.
    /// Each redirect materialises a new Http10Engine pipeline since HTTP/1.0 has no keep-alive by default.
    /// </summary>
    private async Task<HttpResponseMessage> SendWithRedirectAsync(
        HttpRequestMessage request,
        RedirectHandler? handler = null)
    {
        handler ??= new RedirectHandler();
        var current = request;

        for (var i = 0; i <= RedirectPolicy.Default.MaxRedirects; i++)
        {
            var response = await SendAsync(current);
            response.RequestMessage = current;

            if (!RedirectHandler.IsRedirect(response))
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

    [Theory(DisplayName = "REDIR-INT-001: GET 301/302 redirect follows Location to /hello")]
    [InlineData(301)]
    [InlineData(302)]
    public async Task Get_301_302_FollowsRedirect(int statusCode)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"http://127.0.0.1:{_fixture.Port}/redirect/{statusCode}/hello")
        {
            Version = HttpVersion.Version10
        };

        var response = await SendWithRedirectAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("Hello World", body);
    }

    [Fact(DisplayName = "REDIR-INT-002: POST 302 rewrites method to GET (RFC 9110 §15.4.3)")]
    public async Task Post_302_RewritesToGet()
    {
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"http://127.0.0.1:{_fixture.Port}/redirect/302")
        {
            Version = HttpVersion.Version10,
            Content = new StringContent("should-be-dropped")
        };

        var response = await SendWithRedirectAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        // 302 rewrites POST → GET, redirects to /hello which returns "Hello World"
        Assert.Equal("Hello World", body);
    }

    [Fact(DisplayName = "REDIR-INT-003: Redirect chain (3 hops) follows to final /hello")]
    public async Task RedirectChain_3Hops_FollowsToEnd()
    {
        var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"http://127.0.0.1:{_fixture.Port}/redirect/chain/3")
        {
            Version = HttpVersion.Version10
        };

        var response = await SendWithRedirectAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("Hello World", body);
    }

    [Fact(DisplayName = "REDIR-INT-004: Redirect loop detected — throws RedirectException")]
    public async Task RedirectLoop_Detected()
    {
        var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"http://127.0.0.1:{_fixture.Port}/redirect/loop")
        {
            Version = HttpVersion.Version10
        };

        var ex = await Assert.ThrowsAsync<RedirectException>(
            () => SendWithRedirectAsync(request));

        Assert.Equal(RedirectError.RedirectLoop, ex.Error);
    }

    [Fact(DisplayName = "REDIR-INT-005: Max redirect limit enforced — throws RedirectException")]
    public async Task MaxRedirectLimit_Enforced()
    {
        var policy = new RedirectPolicy { MaxRedirects = 1 };
        var handler = new RedirectHandler(policy);

        // chain/3 needs 3 hops but limit is 1
        var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"http://127.0.0.1:{_fixture.Port}/redirect/chain/3")
        {
            Version = HttpVersion.Version10
        };

        var ex = await Assert.ThrowsAsync<RedirectException>(
            () => SendWithRedirectAsync(request, handler));

        Assert.Equal(RedirectError.MaxRedirectsExceeded, ex.Error);
    }

    [Fact(DisplayName = "REDIR-INT-006: Relative Location header resolved correctly")]
    public async Task RelativeLocation_ResolvedCorrectly()
    {
        // /redirect/relative returns Location: hello (relative)
        // Resolved against http://127.0.0.1:{port}/redirect/relative → /redirect/hello
        // /redirect/hello has no matching route (code must be int) → 404
        // We verify the redirect was followed (no longer 302) and the resolved URI is correct.
        var handler = new RedirectHandler();
        var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"http://127.0.0.1:{_fixture.Port}/redirect/relative")
        {
            Version = HttpVersion.Version10
        };

        var response = await SendWithRedirectAsync(request, handler);

        // The redirect was followed — response is not a 3xx
        Assert.False(RedirectHandler.IsRedirect(response),
            "Redirect should have been followed");
        // The resolved URI should be the relative "hello" resolved against the original path
        Assert.EndsWith("/redirect/hello", response.RequestMessage?.RequestUri?.AbsolutePath ?? "");
    }

    [Fact(DisplayName = "REDIR-INT-007: Cross-origin redirect strips Authorization header")]
    public async Task CrossOrigin_StripsAuthorization()
    {
        // Connect via localhost, redirect goes to 127.0.0.1 (different origin)
        var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"http://localhost:{_fixture.Port}/redirect/cross-origin")
        {
            Version = HttpVersion.Version10
        };
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer secret-token");

        var response = await SendWithRedirectAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // /headers/echo echoes back request headers as response headers
        // Authorization should NOT be present after cross-origin redirect
        Assert.False(
            response.Headers.Contains("Authorization"),
            "Authorization header must be stripped on cross-origin redirect");
    }
}
