using System.Net;
using Akka.Streams.Dsl;
using TurboHTTP.Protocol.Semantics;
using TurboHTTP.Streams.Stages.Features;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.AcceptanceTests.H11;

public sealed class RedirectSecuritySpec : AcceptanceTestBase
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

    private static HttpResponseMessage RedirectResponse(HttpStatusCode code, string location)
    {
        var r = new HttpResponseMessage(code);
        r.Headers.Location = new Uri(location, UriKind.RelativeOrAbsolute);
        return r;
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-15.4")]
    public async Task RedirectSecurity_should_handle_self_redirect_loop_gracefully()
    {
        var map = new ResponseMap()
            .On("/redirect/loop", _ => RedirectResponse(HttpStatusCode.Found,
                "http://localhost/redirect/loop"));

        var response = await SendAsync(map,
            new HttpRequestMessage(HttpMethod.Get, "http://localhost/redirect/loop"));

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-15.4")]
    public async Task RedirectSecurity_should_follow_redirect_chain_of_4_hops()
    {
        var map = new ResponseMap()
            .On("/hello", HttpStatusCode.OK, "Hello World")
            .On(req => req.RequestUri?.AbsolutePath.StartsWith("/redirect/chain/") == true, req =>
            {
                var n = int.Parse(req.RequestUri!.Segments.Last().TrimEnd('/'));
                return n <= 1
                    ? RedirectResponse(HttpStatusCode.Found, "http://localhost/hello")
                    : RedirectResponse(HttpStatusCode.Found, $"http://localhost/redirect/chain/{n - 1}");
            });

        var response = await SendAsync(map,
            new HttpRequestMessage(HttpMethod.Get, "http://localhost/redirect/chain/4"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal("Hello World", body);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-15.4")]
    public async Task RedirectSecurity_should_reject_redirect_chain_exceeding_max_hops()
    {
        var map = new ResponseMap()
            .On(req => req.RequestUri?.AbsolutePath.StartsWith("/redirect/chain/") == true, req =>
            {
                var n = int.Parse(req.RequestUri!.Segments.Last().TrimEnd('/'));
                return n <= 1
                    ? RedirectResponse(HttpStatusCode.Found, "http://localhost/hello")
                    : RedirectResponse(HttpStatusCode.Found, $"http://localhost/redirect/chain/{n - 1}");
            });

        var response = await SendAsync(map,
            new HttpRequestMessage(HttpMethod.Get, "http://localhost/redirect/chain/11"));

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
    }
}