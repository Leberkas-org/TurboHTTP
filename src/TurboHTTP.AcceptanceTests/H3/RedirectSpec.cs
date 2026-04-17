using System.Net;
using System.Text;
using Akka.Streams.Dsl;
using TurboHTTP.Protocol.Semantics;
using TurboHTTP.Streams.Stages.Features;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.AcceptanceTests.H3;

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
    public async Task Get_301_should_follow_redirect_to_hello()
    {
        var map = CreateBaseMap()
            .On("/redirect/301/hello", _ => RedirectResponse(HttpStatusCode.MovedPermanently,
                "http://localhost/hello"));

        var response = await SendAsync(map,
            new HttpRequestMessage(HttpMethod.Get, "http://localhost/redirect/301/hello"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal("Hello World", body);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-15.4")]
    public async Task Get_302_should_follow_redirect_to_hello()
    {
        var map = CreateBaseMap()
            .On("/redirect/302/hello", _ => RedirectResponse(HttpStatusCode.Found,
                "http://localhost/hello"));

        var response = await SendAsync(map,
            new HttpRequestMessage(HttpMethod.Get, "http://localhost/redirect/302/hello"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal("Hello World", body);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-15.4")]
    public async Task Get_303_should_follow_redirect_to_hello()
    {
        var map = CreateBaseMap()
            .On("/redirect/303/hello", _ => RedirectResponse(HttpStatusCode.SeeOther,
                "http://localhost/hello"));

        var response = await SendAsync(map,
            new HttpRequestMessage(HttpMethod.Get, "http://localhost/redirect/303/hello"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal("Hello World", body);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-15.4")]
    public async Task Get_307_should_follow_redirect_to_hello()
    {
        var map = CreateBaseMap()
            .On("/redirect/307/hello", _ => RedirectResponse(HttpStatusCode.TemporaryRedirect,
                "http://localhost/hello"));

        var response = await SendAsync(map,
            new HttpRequestMessage(HttpMethod.Get, "http://localhost/redirect/307/hello"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal("Hello World", body);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-15.4")]
    public async Task Get_308_should_follow_redirect_to_hello()
    {
        var map = CreateBaseMap()
            .On("/redirect/308/hello", _ => RedirectResponse(HttpStatusCode.PermanentRedirect,
                "http://localhost/hello"));

        var response = await SendAsync(map,
            new HttpRequestMessage(HttpMethod.Get, "http://localhost/redirect/308/hello"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal("Hello World", body);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-15.4")]
    public async Task Post_303_should_rewrite_to_get()
    {
        var map = CreateBaseMap()
            .On("/redirect/303", _ => RedirectResponse(HttpStatusCode.SeeOther,
                "http://localhost/hello"));

        var request = new HttpRequestMessage(HttpMethod.Post, "http://localhost/redirect/303")
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
    public async Task Post_302_should_rewrite_to_get()
    {
        var map = CreateBaseMap()
            .On("/redirect/302", _ => RedirectResponse(HttpStatusCode.Found,
                "http://localhost/hello"));

        var request = new HttpRequestMessage(HttpMethod.Post, "http://localhost/redirect/302")
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
    public async Task Post_307_should_preserve_method_and_body()
    {
        var map = CreateBaseMap()
            .On("/redirect/307", _ => RedirectResponse(HttpStatusCode.TemporaryRedirect,
                "http://localhost/echo"));

        var payload = "redirect-307-body";
        var request = new HttpRequestMessage(HttpMethod.Post, "http://localhost/redirect/307")
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
    public async Task Post_308_should_preserve_method_and_body()
    {
        var map = CreateBaseMap()
            .On("/redirect/308", _ => RedirectResponse(HttpStatusCode.PermanentRedirect,
                "http://localhost/echo"));

        var payload = "redirect-308-body";
        var request = new HttpRequestMessage(HttpMethod.Post, "http://localhost/redirect/308")
        {
            Content = new StringContent(payload, Encoding.UTF8, "text/plain")
        };
        var response = await SendAsync(map, request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal(payload, body);
    }

    [Theory(Timeout = 5000)]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(5)]
    [Trait("RFC", "RFC9110-15.4")]
    public async Task Redirect_chain_should_follow_N_hops_to_hello(int hops)
    {
        var map = CreateBaseMap()
            .On(req => req.RequestUri?.AbsolutePath.StartsWith("/redirect/chain/") == true, req =>
            {
                var n = int.Parse(req.RequestUri!.Segments.Last().TrimEnd('/'));
                return n <= 1
                    ? RedirectResponse(HttpStatusCode.Found, "http://localhost/hello")
                    : RedirectResponse(HttpStatusCode.Found, $"http://localhost/redirect/chain/{n - 1}");
            });

        var response = await SendAsync(map,
            new HttpRequestMessage(HttpMethod.Get, $"http://localhost/redirect/chain/{hops}"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal("Hello World", body);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-15.4")]
    public async Task Infinite_redirect_loop_should_return_final_redirect_response()
    {
        var map = new ResponseMap()
            .On("/redirect/loop", _ => RedirectResponse(HttpStatusCode.Found,
                "http://localhost/redirect/loop"));

        var response = await SendAsync(map,
            new HttpRequestMessage(HttpMethod.Get, "http://localhost/redirect/loop"));

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
    }
}
