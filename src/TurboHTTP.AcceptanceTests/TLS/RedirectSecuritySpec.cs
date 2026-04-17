using System.Net;
using Akka.Streams.Dsl;
using TurboHTTP.Protocol.Semantics;
using TurboHTTP.Streams.Stages.Features;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.AcceptanceTests.TLS;

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
    public async Task Https_to_http_301_downgrade_should_be_blocked()
    {
        var map = new ResponseMap()
            .On("/redirect/cross-scheme/301", _ => RedirectResponse(HttpStatusCode.MovedPermanently,
                "http://localhost/hello"));

        var response = await SendAsync(map,
            new HttpRequestMessage(HttpMethod.Get, "https://localhost/redirect/cross-scheme/301"));

        Assert.Equal(HttpStatusCode.MovedPermanently, response.StatusCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-15.4")]
    public async Task Https_to_http_302_downgrade_should_be_blocked()
    {
        var map = new ResponseMap()
            .On("/redirect/cross-scheme", _ => RedirectResponse(HttpStatusCode.Found,
                "http://localhost/hello"));

        var response = await SendAsync(map,
            new HttpRequestMessage(HttpMethod.Get, "https://localhost/redirect/cross-scheme"));

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
    }
}
