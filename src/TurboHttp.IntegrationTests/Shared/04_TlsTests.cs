using System.Net;
using System.Net.Security;
using Akka.Actor;
using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHttp.IntegrationTests.Shared;
using TurboHttp.IO;
using TurboHttp.IO.Stages;
using TurboHttp.Protocol;
using TurboHttp.Streams;

namespace TurboHttp.IntegrationTests.Shared;

/// <summary>
/// Integration tests for TLS behaviour.
/// Verifies HTTPS with self-signed certificates, Secure cookie enforcement,
/// HTTPS redirect preservation, HTTP→HTTPS upgrade, and HTTPS→HTTP downgrade blocking.
/// </summary>
public sealed class TlsTests : TestKit, IClassFixture<KestrelTlsFixture>
{
    private readonly KestrelTlsFixture _fixture;
    private readonly IMaterializer _materializer;
    private readonly IActorRef _clientManager;

    public TlsTests(KestrelTlsFixture fixture)
    {
        _fixture = fixture;
        _materializer = Sys.Materializer();
        _clientManager = Sys.ActorOf(Props.Create<ClientManager>());
    }

    /// <summary>
    /// Sends a single HTTP/1.1 request over TLS through the Http11Engine pipeline.
    /// Uses TlsOptions with a callback that accepts the self-signed certificate.
    /// </summary>
    private async Task<HttpResponseMessage> SendTlsAsync(HttpRequestMessage request)
    {
        var engine = new Http11Engine();
        var tlsOptions = new TlsOptions
        {
            Host = "127.0.0.1",
            Port = _fixture.Port,
            TargetHost = "localhost",
            ServerCertificateValidationCallback = (_, _, _, _) => true,
            EnabledSslProtocols = System.Security.Authentication.SslProtocols.None
        };

        var transport =
            Flow.Create<ITransportItem>()
                .Prepend(Source.Single<ITransportItem>(
                    new ConnectItem(tlsOptions)))
                .Via(new ConnectionStage(_clientManager));

        var flow = engine.CreateFlow().Join(transport);

        var (queue, responseTask) = Source.Queue<HttpRequestMessage>(1, OverflowStrategy.Backpressure)
            .Via(flow)
            .ToMaterialized(Sink.First<HttpResponseMessage>(), Keep.Both)
            .Run(_materializer);

        await queue.OfferAsync(request);
        return await responseTask.WaitAsync(TimeSpan.FromSeconds(10));
    }

    private HttpRequestMessage MakeGet(string path) =>
        new(HttpMethod.Get, $"https://localhost:{_fixture.Port}{path}")
        {
            Version = HttpVersion.Version11
        };

    // ── Tests ────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "TLS-001: HTTPS self-signed — connection succeeds with custom cert callback")]
    public async Task Https_SelfSigned_ConnectionSucceeds()
    {
        var request = MakeGet("/hello");
        var response = await SendTlsAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("Hello World", body);
    }

    [Fact(DisplayName = "TLS-002: Secure cookie — only sent over HTTPS, not HTTP")]
    public async Task SecureCookie_OnlySentOverHttps()
    {
        var jar = new CookieJar();

        // Simulate a Set-Cookie: secure_tok=abc; Secure response from an HTTPS origin
        var httpsUri = new Uri($"https://localhost:{_fixture.Port}/");
        var setResponse = new HttpResponseMessage(HttpStatusCode.OK);
        setResponse.Headers.TryAddWithoutValidation("Set-Cookie", "secure_tok=abc; Secure; Path=/");
        jar.ProcessResponse(httpsUri, setResponse);

        Assert.Equal(1, jar.Count);

        // Cookie SHOULD be injected for HTTPS request
        var httpsRequest = new HttpRequestMessage(HttpMethod.Get, httpsUri);
        jar.AddCookiesToRequest(httpsUri, ref httpsRequest);
        Assert.True(
            httpsRequest.Headers.TryGetValues("Cookie", out var cookieValues),
            "Secure cookie should be sent over HTTPS");
        Assert.Contains("secure_tok=abc", string.Join("; ", cookieValues));

        // Cookie SHOULD NOT be injected for HTTP request (same host, different scheme)
        var httpUri = new Uri($"http://localhost:{_fixture.Port}/");
        var httpRequest = new HttpRequestMessage(HttpMethod.Get, httpUri);
        jar.AddCookiesToRequest(httpUri, ref httpRequest);
        Assert.False(
            httpRequest.Headers.TryGetValues("Cookie", out _),
            "Secure cookie must not be sent over plain HTTP (RFC 6265 §5.4)");
    }

    [Fact(DisplayName = "TLS-003: HTTPS redirect — redirect within HTTPS preserves TLS")]
    public async Task HttpsRedirect_PreservesTls()
    {
        var handler = new RedirectHandler();

        var originalUri = new Uri($"https://localhost:{_fixture.Port}/start");
        var original = new HttpRequestMessage(HttpMethod.Get, originalUri)
        {
            Version = HttpVersion.Version11
        };

        // Simulate a 302 redirect to another HTTPS path on the same host
        var redirectResponse = new HttpResponseMessage(HttpStatusCode.Found);
        redirectResponse.Headers.Location = new Uri($"https://localhost:{_fixture.Port}/hello");

        var redirected = handler.BuildRedirectRequest(original, redirectResponse);

        Assert.Equal("https", redirected.RequestUri!.Scheme);
        Assert.Equal($"https://localhost:{_fixture.Port}/hello", redirected.RequestUri.ToString());

        // Verify the redirected request actually works over TLS
        var response = await SendTlsAsync(redirected);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("Hello World", body);
    }

    [Fact(DisplayName = "TLS-004: HTTP→HTTPS upgrade — redirect from HTTP to HTTPS succeeds")]
    public async Task HttpToHttpsUpgrade_Succeeds()
    {
        var handler = new RedirectHandler();

        // Original request is plain HTTP
        var originalUri = new Uri($"http://localhost:{_fixture.Port}/start");
        var original = new HttpRequestMessage(HttpMethod.Get, originalUri)
        {
            Version = HttpVersion.Version11
        };

        // Server redirects to HTTPS (upgrade)
        var redirectResponse = new HttpResponseMessage(HttpStatusCode.MovedPermanently);
        redirectResponse.Headers.Location = new Uri($"https://localhost:{_fixture.Port}/hello");

        var redirected = handler.BuildRedirectRequest(original, redirectResponse);

        // Redirect should succeed — upgrading HTTP→HTTPS is always allowed
        Assert.Equal("https", redirected.RequestUri!.Scheme);
        Assert.Equal($"https://localhost:{_fixture.Port}/hello", redirected.RequestUri.ToString());

        // Verify the upgraded request works over TLS
        var response = await SendTlsAsync(redirected);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("Hello World", body);
    }

    [Fact(DisplayName = "TLS-005: HTTPS→HTTP blocked — redirect downgrade throws RedirectDowngradeException")]
    public void HttpsToHttpDowngrade_Blocked()
    {
        var handler = new RedirectHandler();

        // Original request is HTTPS
        var originalUri = new Uri($"https://localhost:{_fixture.Port}/secure");
        var original = new HttpRequestMessage(HttpMethod.Get, originalUri)
        {
            Version = HttpVersion.Version11
        };

        // Server tries to redirect to plain HTTP (downgrade)
        var redirectResponse = new HttpResponseMessage(HttpStatusCode.Found);
        redirectResponse.Headers.Location = new Uri($"http://localhost:{_fixture.Port}/insecure");

        // Default policy blocks HTTPS→HTTP downgrade
        var ex = Assert.Throws<RedirectDowngradeException>(
            () => handler.BuildRedirectRequest(original, redirectResponse));

        Assert.Contains("HTTPS to HTTP", ex.Message);
        Assert.Contains("downgrade", ex.Message);
    }
}
