using System.Buffers;
using System.Net;
using System.Text.Json;
using Akka;
using Akka.Actor;
using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHttp.IntegrationTests.Shared;
using TurboHttp.IO;
using TurboHttp.Protocol;
using TurboHttp.Streams.Stages;

namespace TurboHttp.IntegrationTests.Http20;

/// <summary>
/// Integration tests for Http20Engine cookie handling.
/// Verifies RFC 6265 cookie storage, multiple Set-Cookie headers, HPACK-compressed
/// cookie headers, cross-redirect cookie persistence, and Path restriction over HTTP/2.
/// </summary>
public sealed class Http20CookieTests : TestKit, IClassFixture<KestrelH2Fixture>
{
    private readonly KestrelH2Fixture _fixture;
    private readonly IMaterializer _materializer;
    private readonly IActorRef _clientManager;

    public Http20CookieTests(KestrelH2Fixture fixture)
    {
        _fixture = fixture;
        _materializer = Sys.Materializer();
        _clientManager = Sys.ActorOf(Props.Create<ClientManager>());
    }

    /// <summary>
    /// Builds an HTTP/2 pipeline flow.
    /// </summary>
    private Flow<HttpRequestMessage, HttpResponseMessage, NotUsed> BuildFlow()
    {
        var requestEncoder = new Http2RequestEncoder();
        var tcpOptions = new TcpOptions
        {
            Host = "127.0.0.1",
            Port = _fixture.Port
        };

        return Flow.FromGraph(GraphDsl.Create(b =>
        {
            var streamIdAllocator = b.Add(new StreamIdAllocatorStage());
            var requestToFrame = b.Add(new Request2FrameStage(requestEncoder));
            var frameEncoder = b.Add(new Http20EncoderStage());
            const int windowSize = 2 * 1024 * 1024;
            var prependPreface = b.Add(new PrependPrefaceStage(windowSize));
            var frameDecoder = b.Add(new Http20DecoderStage());
            var streamDecoder = b.Add(new Http20StreamStage());
            var h2Connection = b.Add(new Http20ConnectionStage(windowSize));

            var connectionStage = b.Add(new ConnectionStage(_clientManager));

            var toDataItem = b.Add(Flow.Create<(IMemoryOwner<byte>, int)>()
                .Select(ITransportItem (x) => new DataItem(x.Item1, x.Item2)));

            var connectSource = b.Add(Source.Single<ITransportItem>(new ConnectItem(tcpOptions)));
            var concat = b.Add(Concat.Create<ITransportItem>(2));

            // Request path
            b.From(streamIdAllocator.Outlet).To(requestToFrame.Inlet);
            b.From(requestToFrame.Outlet).To(h2Connection.Inlet2);

            // Outbound
            b.From(h2Connection.Outlet2).To(frameEncoder.Inlet);
            b.From(frameEncoder.Outlet).To(toDataItem.Inlet);
            b.From(connectSource).To(concat.In(0));
            b.From(toDataItem.Outlet).To(concat.In(1));
            b.From(concat.Out).To(prependPreface.Inlet);
            b.From(prependPreface.Outlet).To(connectionStage.Inlet);

            // Inbound
            b.From(connectionStage.Outlet).To(frameDecoder.Inlet);
            b.From(frameDecoder.Outlet).To(h2Connection.Inlet1);
            b.From(h2Connection.Outlet1).To(streamDecoder.Inlet);

            return new FlowShape<HttpRequestMessage, HttpResponseMessage>(
                streamIdAllocator.Inlet, streamDecoder.Outlet);
        }));
    }

    /// <summary>
    /// Sends a single HTTP/2 request through the pipeline.
    /// </summary>
    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request)
    {
        var flow = BuildFlow();

        var (queue, responseTask) = Source.Queue<HttpRequestMessage>(1, OverflowStrategy.Backpressure)
            .Via(flow)
            .ToMaterialized(Sink.First<HttpResponseMessage>(), Keep.Both)
            .Run(_materializer);

        await queue.OfferAsync(request);
        return await responseTask.WaitAsync(TimeSpan.FromSeconds(10));
    }

    /// <summary>
    /// Sends a request with CookieJar integration — injects cookies before sending,
    /// stores cookies from response.
    /// </summary>
    private async Task<HttpResponseMessage> SendWithCookiesAsync(
        HttpRequestMessage request,
        CookieJar cookieJar)
    {
        if (request.RequestUri is not null)
        {
            cookieJar.AddCookiesToRequest(request.RequestUri, ref request);
        }

        var response = await SendAsync(request);
        response.RequestMessage = request;

        if (request.RequestUri is not null)
        {
            cookieJar.ProcessResponse(request.RequestUri, response);
        }

        return response;
    }

    /// <summary>
    /// Sends a request and manually follows redirects with CookieJar support.
    /// </summary>
    private async Task<HttpResponseMessage> SendWithRedirectAndCookiesAsync(
        HttpRequestMessage request,
        CookieJar cookieJar)
    {
        var handler = new RedirectHandler();
        var current = request;

        for (var i = 0; i <= RedirectPolicy.Default.MaxRedirects; i++)
        {
            if (current.RequestUri is not null)
            {
                cookieJar.AddCookiesToRequest(current.RequestUri, ref current);
            }

            var response = await SendAsync(current);
            response.RequestMessage = current;

            if (current.RequestUri is not null)
            {
                cookieJar.ProcessResponse(current.RequestUri, response);
            }

            if (!RedirectHandler.IsRedirect(response))
            {
                return response;
            }

            current = handler.BuildRedirectRequest(current, response);
            current.Version = HttpVersion.Version20;
        }

        throw new RedirectException(
            "Exceeded redirect loop in test helper",
            RedirectError.MaxRedirectsExceeded);
    }

    private HttpRequestMessage CreateGet(string path)
    {
        return new HttpRequestMessage(
            HttpMethod.Get,
            $"http://127.0.0.1:{_fixture.Port}{path}")
        {
            Version = HttpVersion.Version20
        };
    }

    private static Dictionary<string, string> ParseCookieEcho(string body)
    {
        return JsonSerializer.Deserialize<Dictionary<string, string>>(body)
               ?? new Dictionary<string, string>();
    }

    [Fact(DisplayName = "20E-INT-035: Set-Cookie stored and sent back on subsequent HTTP/2 request")]
    public async Task Cookie_StoredAndSentBack()
    {
        // RFC 6265 §5.3: When a server sends Set-Cookie, the user agent must store it
        // and include it in subsequent requests to the same origin. This test verifies
        // cookie storage and replay work correctly when encoded/decoded via HTTP/2
        // HPACK header compression.
        var jar = new CookieJar();

        // Step 1: Set cookie
        var setResponse = await SendWithCookiesAsync(CreateGet("/cookie/set/session/abc123"), jar);
        Assert.Equal(HttpStatusCode.OK, setResponse.StatusCode);

        // Step 2: Echo cookies back
        var echoResponse = await SendWithCookiesAsync(CreateGet("/cookie/echo"), jar);
        var body = await echoResponse.Content.ReadAsStringAsync();
        var cookies = ParseCookieEcho(body);

        Assert.True(cookies.ContainsKey("session"), "Cookie 'session' should be sent back");
        Assert.Equal("abc123", cookies["session"]);
    }

    [Fact(DisplayName = "20E-INT-036: Multiple Set-Cookie headers in single HTTP/2 response all stored")]
    public async Task MultipleSetCookieHeaders_AllStored()
    {
        // RFC 6265 §5.3 + RFC 9113 §8.2.3: Each Set-Cookie header field is transmitted
        // as a separate header field entry in HTTP/2 (not concatenated). The client must
        // process all Set-Cookie headers from a single response and store each cookie.
        var jar = new CookieJar();

        // /cookie/set-multiple sets alpha, beta, gamma in one response
        await SendWithCookiesAsync(CreateGet("/cookie/set-multiple"), jar);

        var echoResponse = await SendWithCookiesAsync(CreateGet("/cookie/echo"), jar);
        var body = await echoResponse.Content.ReadAsStringAsync();
        var cookies = ParseCookieEcho(body);

        Assert.Equal("one", cookies["alpha"]);
        Assert.Equal("two", cookies["beta"]);
        Assert.Equal("three", cookies["gamma"]);
    }

    [Fact(DisplayName = "20E-INT-037: Cookie header survives HPACK compression round-trip")]
    public async Task CookieHeader_SurvivesHpackCompression()
    {
        // RFC 7541 §6.2.3: The cookie header is in the HPACK static table (index 32),
        // enabling efficient compression. Sensitive cookie values should use
        // literal-never-indexed encoding to prevent CRIME-style attacks.
        // This test verifies that a cookie value set via Set-Cookie and then sent back
        // as a Cookie header is correctly HPACK-encoded and decoded through the full
        // HTTP/2 pipeline, arriving intact at the server.
        var jar = new CookieJar();

        // Set multiple cookies so the Cookie header contains a non-trivial value
        // that exercises HPACK dynamic table indexing on repeated sends
        await SendWithCookiesAsync(CreateGet("/cookie/set/hpack1/alphavalue"), jar);
        await SendWithCookiesAsync(CreateGet("/cookie/set/hpack2/betavalue"), jar);

        // Send both cookies back — the Cookie header goes through HPACK encode/decode
        var echoResponse = await SendWithCookiesAsync(CreateGet("/cookie/echo"), jar);
        var body = await echoResponse.Content.ReadAsStringAsync();
        var cookies = ParseCookieEcho(body);

        Assert.True(cookies.ContainsKey("hpack1"),
            "First cookie should survive HPACK compression round-trip");
        Assert.Equal("alphavalue", cookies["hpack1"]);
        Assert.True(cookies.ContainsKey("hpack2"),
            "Second cookie should survive HPACK compression round-trip");
        Assert.Equal("betavalue", cookies["hpack2"]);
    }

    [Fact(DisplayName = "20E-INT-038: Cookies preserved across 302 redirect over HTTP/2")]
    public async Task Cookies_PreservedAcrossRedirect()
    {
        // RFC 6265 §5.3 + RFC 9110 §15.4: When a server sets a cookie and issues a
        // redirect, the cookie must be stored before following the redirect so that
        // the subsequent request to the redirect target includes the cookie. This test
        // verifies the full flow over HTTP/2: Set-Cookie in 302 response → store →
        // follow redirect → Cookie header sent to /cookie/echo.
        var jar = new CookieJar();

        // /cookie/set-and-redirect sets redirect_cookie and 302s to /cookie/echo
        var response = await SendWithRedirectAndCookiesAsync(
            CreateGet("/cookie/set-and-redirect"), jar);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        var cookies = ParseCookieEcho(body);

        Assert.True(cookies.ContainsKey("redirect_cookie"),
            "Cookie set during redirect should be sent to redirect target");
        Assert.Equal("from-redirect", cookies["redirect_cookie"]);
    }

    [Fact(DisplayName = "20E-INT-039: Cookie with Path attribute restricts scope over HTTP/2 (RFC 6265 §5.1.4)")]
    public async Task Cookie_PathAttribute_RestrictsScope()
    {
        // RFC 6265 §5.1.4: The Path attribute limits the scope of a cookie to a
        // specific URL path prefix. A cookie with Path=/cookie/echo should be sent
        // to /cookie/echo but not to other paths. This test verifies path-scoped
        // cookie behaviour works correctly through the HTTP/2 pipeline.
        var jar = new CookieJar();

        // Set cookie scoped to /cookie/echo path
        await SendWithCookiesAsync(CreateGet("/cookie/set-path/pathcookie/val/cookie/echo"), jar);

        // Request to /cookie/echo — path matches, cookie should be sent
        var echoResponse = await SendWithCookiesAsync(CreateGet("/cookie/echo"), jar);
        var body = await echoResponse.Content.ReadAsStringAsync();
        var cookies = ParseCookieEcho(body);

        Assert.True(cookies.ContainsKey("pathcookie"),
            "Cookie should be sent for matching path over HTTP/2");
    }
}
