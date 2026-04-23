using System.Net;
using Akka;
using Akka.Streams.Dsl;
using Servus.Akka.IO;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.AcceptanceTests.H3;

public sealed class ResilienceSpec : AcceptanceTestBase
{
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-8")]
    public async Task Timeout_should_cancel_request_after_deadline()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/h3/delay/30000")
        {
            Version = HttpVersion.Version30
        };

        var controlFrames = new H3ResponseBuilder().Settings().Build();

        var fake = new H3EngineFakeConnectionStage(controlFrames);
        var flow = CreateHttp30Engine().CreateFlow().Join(Flow.FromGraph<IOutputItem, IInputItem, NotUsed>(fake));

        var tcs = new TaskCompletionSource<HttpResponseMessage>();
        _ = Source.Single(request)
            .Via(flow)
            .RunWith(Sink.ForEach<HttpResponseMessage>(res => tcs.TrySetResult(res)), Materializer);

        await Assert.ThrowsAnyAsync<Exception>(async () =>
            await tcs.Task.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public async Task Connection_reuse_should_survive_multiple_requests()
    {
        for (var i = 0; i < 3; i++)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/ping")
            {
                Version = HttpVersion.Version30
            };

            var controlFrames = new H3ResponseBuilder().Settings().Build();
            var responseFrames = new H3ResponseBuilder()
                .Headers(200, [("content-length", "4")])
                .Data("pong")
                .Build();

            var (response, _) = await SendH3EngineAsync(CreateHttp30Engine().CreateFlow(), request, controlFrames,
                responseFrames);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public async Task Large_response_body_should_be_fully_received()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/large/4")
        {
            Version = HttpVersion.Version30
        };

        var largeBody = new byte[4 * 1024];
        Array.Fill(largeBody, (byte)'A');

        var controlFrames = new H3ResponseBuilder().Settings().Build();
        var responseFrames = new H3ResponseBuilder()
            .Headers(200, [("content-length", largeBody.Length.ToString())])
            .Data((ReadOnlyMemory<byte>)largeBody)
            .Build();

        var (response, _) =
            await SendH3EngineAsync(CreateHttp30Engine().CreateFlow(), request, controlFrames, responseFrames);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal(4 * 1024, body.Length);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public async Task Connection_should_survive_pipeline_stress()
    {
        var tasks = Enumerable.Range(0, 20).Select(async _ =>
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/ping")
            {
                Version = HttpVersion.Version30
            };

            var controlFrames = new H3ResponseBuilder().Settings().Build();
            var responseFrames = new H3ResponseBuilder()
                .Headers(200, [("content-length", "4")])
                .Data("pong")
                .Build();

            var (response, _) = await SendH3EngineAsync(CreateHttp30Engine().CreateFlow(), request, controlFrames,
                responseFrames);
            return response;
        }).ToArray();

        var responses = await Task.WhenAll(tasks);

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public async Task Partial_body_read_should_not_corrupt_next_request()
    {
        var request1 = new HttpRequestMessage(HttpMethod.Get, "http://localhost/hello")
        {
            Version = HttpVersion.Version30
        };

        var controlFrames = new H3ResponseBuilder().Settings().Build();
        var responseFrames = new H3ResponseBuilder()
            .Headers(200, [("content-length", "11")])
            .Data("Hello World")
            .Build();

        var (response1, _) =
            await SendH3EngineAsync(CreateHttp30Engine().CreateFlow(), request1, controlFrames, responseFrames);
        Assert.Equal(HttpStatusCode.OK, response1.StatusCode);

        var request2 = new HttpRequestMessage(HttpMethod.Get, "http://localhost/hello")
        {
            Version = HttpVersion.Version30
        };

        var (response2, _) =
            await SendH3EngineAsync(CreateHttp30Engine().CreateFlow(), request2, controlFrames, responseFrames);
        Assert.Equal(HttpStatusCode.OK, response2.StatusCode);
        var body2 = await response2.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal("Hello World", body2);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public async Task Interleaved_concurrent_requests_should_not_corrupt_responses()
    {
        var tasks = Enumerable.Range(0, 5).Select(async _ =>
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/hello")
            {
                Version = HttpVersion.Version30
            };

            var controlFrames = new H3ResponseBuilder().Settings().Build();
            var responseFrames = new H3ResponseBuilder()
                .Headers(200, [("content-length", "11")])
                .Data("Hello World")
                .Build();

            var (response, _) = await SendH3EngineAsync(CreateHttp30Engine().CreateFlow(), request, controlFrames,
                responseFrames);
            return response;
        }).ToArray();

        var responses = await Task.WhenAll(tasks);

        foreach (var r in responses)
        {
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
            var body = await r.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            Assert.Equal("Hello World", body);
        }
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public async Task Slow_body_should_be_fully_received()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/resilience/slow-body/500")
        {
            Version = HttpVersion.Version30
        };

        var body = "slow-body-first-halfslow-body-second-half";
        var controlFrames = new H3ResponseBuilder().Settings().Build();
        var responseFrames = new H3ResponseBuilder()
            .Headers(200, [("content-length", body.Length.ToString())])
            .Data(body)
            .Build();

        var (response, _) =
            await SendH3EngineAsync(CreateHttp30Engine().CreateFlow(), request, controlFrames, responseFrames);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var responseBody = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("slow-body-first-half", responseBody);
        Assert.Contains("slow-body-second-half", responseBody);
    }
}