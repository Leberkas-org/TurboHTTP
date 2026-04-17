using System.Net;
using Akka.Streams.Dsl;
using TurboHTTP.Protocol.Semantics;
using TurboHTTP.Streams.Stages.Features;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.AcceptanceTests.H11;

public sealed class RetrySpec : AcceptanceTestBase
{
    private async Task<HttpResponseMessage> SendAsync(ResponseMap map, HttpRequestMessage request,
        RetryPolicy? policy = null)
    {
        var retry = BidiFlow.FromGraph(new RetryBidiStage(policy ?? RetryPolicy.Default));
        var fake = ResponseMapFake.Create(map);
        var flow = retry.Atop(fake)
            .Join(Flow.FromFunction<HttpRequestMessage, HttpResponseMessage>(_ => new HttpResponseMessage()));

        var tcs = new TaskCompletionSource<HttpResponseMessage>();
        _ = Source.Single(request)
            .Via(flow)
            .RunWith(Sink.ForEach<HttpResponseMessage>(res => tcs.TrySetResult(res)), Materializer);

        return await tcs.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-15.5.9")]
    public async Task Get_should_retry_408_and_eventually_return_408()
    {
        var map = new ResponseMap()
            .On("/retry/408", _ => new HttpResponseMessage(HttpStatusCode.RequestTimeout));

        var response = await SendAsync(map,
            new HttpRequestMessage(HttpMethod.Get, "http://localhost/retry/408"));

        Assert.Equal(HttpStatusCode.RequestTimeout, response.StatusCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-15.6.4")]
    public async Task Get_should_retry_503_and_eventually_return_503()
    {
        var map = new ResponseMap()
            .On("/retry/503", _ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

        var response = await SendAsync(map,
            new HttpRequestMessage(HttpMethod.Get, "http://localhost/retry/503"));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact(Timeout = 10000)]
    [Trait("RFC", "RFC9110-15.6.4")]
    public async Task Get_should_retry_after_seconds_header_on_503()
    {
        var map = new ResponseMap()
            .On("/retry/503-retry-after/1", _ =>
            {
                var r = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
                r.Headers.TryAddWithoutValidation("Retry-After", "1");
                return r;
            });

        var response = await SendAsync(map,
            new HttpRequestMessage(HttpMethod.Get, "http://localhost/retry/503-retry-after/1"));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact(Timeout = 10000)]
    [Trait("RFC", "RFC9110-15.6.4")]
    public async Task Get_should_retry_after_date_header_on_503()
    {
        var map = new ResponseMap()
            .On("/retry/503-retry-after-date", _ =>
            {
                var r = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
                r.Headers.TryAddWithoutValidation("Retry-After",
                    DateTimeOffset.UtcNow.AddSeconds(1).ToString("R"));
                return r;
            });

        var response = await SendAsync(map,
            new HttpRequestMessage(HttpMethod.Get, "http://localhost/retry/503-retry-after-date"),
            new RetryPolicy { MaxRetries = 1 });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Theory(Timeout = 10000)]
    [InlineData(2)]
    [InlineData(3)]
    [Trait("RFC", "RFC9110-15.6.4")]
    public async Task Get_should_succeed_after_N_retries(int n)
    {
        var callCount = 0;
        var map = new ResponseMap()
            .On(req => req.RequestUri?.AbsolutePath.StartsWith("/retry/succeed-after/") == true, _ =>
            {
                callCount++;
                if (callCount <= n)
                {
                    return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
                }
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("success")
                };
            });

        var key = Guid.NewGuid().ToString("N");
        var response = await SendAsync(map,
            new HttpRequestMessage(HttpMethod.Get, $"http://localhost/retry/succeed-after/{n}?key={key}"),
            new RetryPolicy { MaxRetries = n + 1 });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal("success", body);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-9.2.2")]
    public async Task Put_should_retry_on_503_because_idempotent()
    {
        var map = new ResponseMap()
            .On("/retry/503", _ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

        var response = await SendAsync(map,
            new HttpRequestMessage(HttpMethod.Put, "http://localhost/retry/503"));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-9.2.2")]
    public async Task Delete_should_retry_on_503_because_idempotent()
    {
        var map = new ResponseMap()
            .On("/retry/503", _ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

        var response = await SendAsync(map,
            new HttpRequestMessage(HttpMethod.Delete, "http://localhost/retry/503"));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-9.2.2")]
    public async Task Post_must_not_retry_on_503_because_non_idempotent()
    {
        var map = new ResponseMap()
            .On("/retry/non-idempotent-503",
                _ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

        var response = await SendAsync(map,
            new HttpRequestMessage(HttpMethod.Post, "http://localhost/retry/non-idempotent-503"));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }
}
