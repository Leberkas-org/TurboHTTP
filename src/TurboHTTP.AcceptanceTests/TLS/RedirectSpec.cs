using System.Net;
using System.Text;
using Akka.Streams.Dsl;
using TurboHTTP.Protocol.Semantics;
using TurboHTTP.Streams.Stages.Features;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.AcceptanceTests.TLS;

public sealed class RedirectSpec : AcceptanceTestBase
{
    private async Task<HttpResponseMessage> SendAsync(ResponseMap map, HttpRequestMessage request,
        RedirectPolicy? policy = null)
    {
        var redirect = BidiFlow.FromGraph(new RedirectBidiStage(policy ?? new RedirectPolicy()));
        var fake = ResponseMapFake.Create(map);
        var flow = redirect.Atop(fake)
            .Join(Flow.FromFunction<HttpRequestMessage, HttpResponseMessage>(_ => new HttpResponseMessage()));

        var tcs = new TaskCompletionSource<HttpResponseMessage>();
        _ = Source.Single(request)
            .Via(flow)
            .RunWith(Sink.ForEach<HttpResponseMessage>(res => tcs.TrySetResult(res)), Materializer);

        return await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    }

    private static ResponseMap CreateBaseMap() => new ResponseMap()
        .On("/hello", HttpStatusCode.OK, "Hello World")
        .On("/echo", req =>
        {
            var body = req.Content?.ReadAsStringAsync().GetAwaiter().GetResult() ?? "";
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body)
            };
        });

    private static HttpResponseMessage RedirectResponse(HttpStatusCode code, string location)
    {
        var r = new HttpResponseMessage(code);
        r.Headers.Location = new Uri(location, UriKind.RelativeOrAbsolute);
        return r;
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-15.4")]
    public async Task Redirect_should_follow_get_301_to_hello_over_https()
    {
        var map = CreateBaseMap()
            .On("/redirect/301/hello", _ => RedirectResponse(HttpStatusCode.MovedPermanently,
                "https://localhost/hello"));

        var response = await SendAsync(map,
            new HttpRequestMessage(HttpMethod.Get, "https://localhost/redirect/301/hello"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal("Hello World", body);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-15.4")]
    public async Task Redirect_should_follow_get_302_to_hello_over_https()
    {
        var map = CreateBaseMap()
            .On("/redirect/302/hello", _ => RedirectResponse(HttpStatusCode.Found,
                "https://localhost/hello"));

        var response = await SendAsync(map,
            new HttpRequestMessage(HttpMethod.Get, "https://localhost/redirect/302/hello"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal("Hello World", body);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-15.4")]
    public async Task Redirect_should_follow_get_307_to_hello_over_https()
    {
        var map = CreateBaseMap()
            .On("/redirect/307/hello", _ => RedirectResponse(HttpStatusCode.TemporaryRedirect,
                "https://localhost/hello"));

        var response = await SendAsync(map,
            new HttpRequestMessage(HttpMethod.Get, "https://localhost/redirect/307/hello"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal("Hello World", body);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-15.4")]
    public async Task Redirect_should_follow_get_308_to_hello_over_https()
    {
        var map = CreateBaseMap()
            .On("/redirect/308/hello", _ => RedirectResponse(HttpStatusCode.PermanentRedirect,
                "https://localhost/hello"));

        var response = await SendAsync(map,
            new HttpRequestMessage(HttpMethod.Get, "https://localhost/redirect/308/hello"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal("Hello World", body);
    }

    [Theory(Timeout = 5000)]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(5)]
    [Trait("RFC", "RFC9110-15.4")]
    public async Task Redirect_chain_should_follow_n_hops_to_hello_over_https(int hops)
    {
        var map = CreateBaseMap()
            .On(req => req.RequestUri?.AbsolutePath.StartsWith("/redirect/chain/") == true, req =>
            {
                var n = int.Parse(req.RequestUri!.Segments.Last().TrimEnd('/'));
                return n <= 1
                    ? RedirectResponse(HttpStatusCode.Found, "https://localhost/hello")
                    : RedirectResponse(HttpStatusCode.Found, $"https://localhost/redirect/chain/{n - 1}");
            });

        var response = await SendAsync(map,
            new HttpRequestMessage(HttpMethod.Get, $"https://localhost/redirect/chain/{hops}"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal("Hello World", body);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-15.4")]
    public async Task Infinite_redirect_loop_should_return_final_redirect_response_over_https()
    {
        var map = new ResponseMap()
            .On("/redirect/loop", _ => RedirectResponse(HttpStatusCode.Found,
                "https://localhost/redirect/loop"));

        var response = await SendAsync(map,
            new HttpRequestMessage(HttpMethod.Get, "https://localhost/redirect/loop"));

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-15.4")]
    public async Task Relative_location_header_should_be_resolved_to_hello_over_https()
    {
        var map = CreateBaseMap()
            .On("/redirect/relative", _ => RedirectResponse(HttpStatusCode.Found, "/hello"));

        var response = await SendAsync(map,
            new HttpRequestMessage(HttpMethod.Get, "https://localhost/redirect/relative"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal("Hello World", body);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-15.4")]
    public async Task Cross_scheme_downgrade_should_be_blocked_over_https()
    {
        var map = CreateBaseMap()
            .On("/redirect/cross-scheme", _ => RedirectResponse(HttpStatusCode.Found,
                "http://localhost/hello"));

        var response = await SendAsync(map,
            new HttpRequestMessage(HttpMethod.Get, "https://localhost/redirect/cross-scheme"));

        Assert.True(
            (int)response.StatusCode >= 300 && (int)response.StatusCode < 400,
            $"Expected a redirect status but got {response.StatusCode}");
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-15.4")]
    public async Task Post_307_should_preserve_method_and_body_to_echo_over_https()
    {
        var map = CreateBaseMap()
            .On("/redirect/307", _ => RedirectResponse(HttpStatusCode.TemporaryRedirect,
                "https://localhost/echo"));

        var payload = "redirect-tls-307-body";
        var request = new HttpRequestMessage(HttpMethod.Post, "https://localhost/redirect/307")
        {
            Content = new StringContent(payload, Encoding.UTF8, "text/plain")
        };
        var response = await SendAsync(map, request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal(payload, body);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-15.4")]
    public async Task Post_303_should_rewrite_to_get_at_hello_over_https()
    {
        var map = CreateBaseMap()
            .On("/redirect/303", _ => RedirectResponse(HttpStatusCode.SeeOther,
                "https://localhost/hello"));

        var request = new HttpRequestMessage(HttpMethod.Post, "https://localhost/redirect/303")
        {
            Content = new StringContent("ignored-body", Encoding.UTF8, "text/plain")
        };
        var response = await SendAsync(map, request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal("Hello World", body);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-15.4")]
    public async Task Post_302_should_rewrite_to_get_at_hello_over_https()
    {
        var map = CreateBaseMap()
            .On("/redirect/302", _ => RedirectResponse(HttpStatusCode.Found,
                "https://localhost/hello"));

        var request = new HttpRequestMessage(HttpMethod.Post, "https://localhost/redirect/302")
        {
            Content = new StringContent("ignored-body", Encoding.UTF8, "text/plain")
        };
        var response = await SendAsync(map, request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal("Hello World", body);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-15.4")]
    public async Task Post_308_should_preserve_method_and_body_to_echo_over_https()
    {
        var map = CreateBaseMap()
            .On("/redirect/308", _ => RedirectResponse(HttpStatusCode.PermanentRedirect,
                "https://localhost/echo"));

        var payload = "redirect-tls-308-body";
        var request = new HttpRequestMessage(HttpMethod.Post, "https://localhost/redirect/308")
        {
            Content = new StringContent(payload, Encoding.UTF8, "text/plain")
        };
        var response = await SendAsync(map, request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal(payload, body);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-15.4")]
    public async Task Cross_origin_redirect_should_be_blocked_as_downgrade_over_https()
    {
        var map = new ResponseMap()
            .On("/redirect/cross-origin", _ => RedirectResponse(HttpStatusCode.Found,
                "http://127.0.0.1/echo"))
            .On("/echo", _ => new HttpResponseMessage(HttpStatusCode.OK));

        var request = new HttpRequestMessage(HttpMethod.Get, "https://localhost/redirect/cross-origin");
        request.Headers.Add("X-Test", "preserved");
        var response = await SendAsync(map, request);

        Assert.True(
            (int)response.StatusCode >= 300 && (int)response.StatusCode < 400,
            $"Expected a redirect status but got {response.StatusCode}");
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-15.4")]
    public async Task Cross_origin_auth_redirect_should_be_blocked_as_downgrade_over_https()
    {
        var map = new ResponseMap()
            .On("/redirect/cross-origin-auth", _ => RedirectResponse(HttpStatusCode.Found,
                "http://127.0.0.1/auth"))
            .On("/auth", req =>
            {
                if (req.Headers.Authorization is not null)
                {
                    return new HttpResponseMessage(HttpStatusCode.OK);
                }
                return new HttpResponseMessage(HttpStatusCode.Unauthorized);
            });

        var request = new HttpRequestMessage(HttpMethod.Get, "https://localhost/redirect/cross-origin-auth");
        request.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "test-token");
        var response = await SendAsync(map, request);

        Assert.True(
            (int)response.StatusCode >= 300 && (int)response.StatusCode < 400,
            $"Expected a redirect status but got {response.StatusCode}");
    }
}
