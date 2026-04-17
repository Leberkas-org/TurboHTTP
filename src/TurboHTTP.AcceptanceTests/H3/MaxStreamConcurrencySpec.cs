using System.Net;
using TurboHTTP.Internal;
using TurboHTTP.Streams;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.AcceptanceTests.H3;

public sealed class MaxStreamConcurrencySpec : AcceptanceTestBase
{
    private static Http30Engine Engine => new(new Http3Options().ToEngineOptions());

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-6.1")]
    public async Task Five_concurrent_requests_should_complete_within_stream_limit()
    {
        var tasks = Enumerable.Range(0, 5).Select(async _ =>
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/h3/delay/200")
            {
                Version = HttpVersion.Version30
            };

            var controlFrames = new H3ResponseBuilder().Settings().Build();
            var responseFrames = new H3ResponseBuilder()
                .Headers(200, [("content-length", "7")])
                .Data("delayed")
                .Build();

            var (response, _) = await SendH3EngineAsync(Engine.CreateFlow(), request, controlFrames, responseFrames);
            return response;
        }).ToArray();

        var responses = await Task.WhenAll(tasks);

        Assert.Equal(5, responses.Length);
        foreach (var response in responses)
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-6.1")]
    public async Task Sequential_requests_should_succeed_through_stream_limiter()
    {
        for (var i = 0; i < 5; i++)
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

            var (response, _) = await SendH3EngineAsync(Engine.CreateFlow(), request, controlFrames, responseFrames);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            Assert.Equal("Hello World", body);
        }
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-6.1")]
    public async Task Ten_concurrent_requests_should_complete_successfully()
    {
        var tasks = Enumerable.Range(0, 10).Select(async _ =>
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

            var (response, _) = await SendH3EngineAsync(Engine.CreateFlow(), request, controlFrames, responseFrames);
            return response;
        }).ToArray();

        var responses = await Task.WhenAll(tasks);

        Assert.Equal(10, responses.Length);
        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-6.1")]
    public async Task Concurrent_delayed_requests_should_complete_as_streams_free_up()
    {
        var tasks = Enumerable.Range(0, 5).Select(async _ =>
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/h3/delay/300")
            {
                Version = HttpVersion.Version30
            };

            var controlFrames = new H3ResponseBuilder().Settings().Build();
            var responseFrames = new H3ResponseBuilder()
                .Headers(200, [("content-length", "7")])
                .Data("delayed")
                .Build();

            var (response, _) = await SendH3EngineAsync(Engine.CreateFlow(), request, controlFrames, responseFrames);
            return response;
        }).ToArray();

        var responses = await Task.WhenAll(tasks);

        Assert.Equal(5, responses.Length);
        foreach (var response in responses)
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            Assert.Equal("delayed", body);
        }
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-6.1")]
    public async Task Mixed_concurrent_requests_should_complete()
    {
        var tasks = new[]
        {
            SendSimpleGetAsync("/hello", "Hello World"),
            SendSimpleGetAsync("/ping", "pong"),
            SendSimpleGetAsync("/h3/delay/100", "delayed"),
            SendSimpleGetAsync("/hello", "Hello World"),
            SendSimpleGetAsync("/ping", "pong")
        };

        var responses = await Task.WhenAll(tasks);

        Assert.Equal(5, responses.Length);
        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
    }

    private async Task<HttpResponseMessage> SendSimpleGetAsync(string path, string responseBody)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"http://localhost{path}")
        {
            Version = HttpVersion.Version30
        };

        var controlFrames = new H3ResponseBuilder().Settings().Build();
        var responseFrames = new H3ResponseBuilder()
            .Headers(200, [("content-length", responseBody.Length.ToString())])
            .Data(responseBody)
            .Build();

        var (response, _) = await SendH3EngineAsync(Engine.CreateFlow(), request, controlFrames, responseFrames);
        return response;
    }
}
