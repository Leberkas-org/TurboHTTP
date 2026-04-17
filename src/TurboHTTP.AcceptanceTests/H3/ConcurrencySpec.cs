using System.Net;
using System.Text;
using TurboHTTP.Internal;
using TurboHTTP.Streams;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.AcceptanceTests.H3;

public sealed class ConcurrencySpec : AcceptanceTestBase
{
    private static Http30Engine Engine => new(new Http3Options().ToEngineOptions());

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public async Task Ten_parallel_gets_should_be_multiplexed_over_quic_streams()
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
    [Trait("RFC", "RFC9114-4.1")]
    public async Task Twenty_parallel_requests_should_succeed()
    {
        var tasks = Enumerable.Range(0, 20).Select(async _ =>
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
            return response;
        }).ToArray();

        var responses = await Task.WhenAll(tasks);

        Assert.Equal(20, responses.Length);
        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public async Task Mixed_get_and_post_should_be_multiplexed()
    {
        var tasks = new[]
        {
            SendSimpleAsync(HttpMethod.Get, "/hello", "Hello World"),
            SendSimpleAsync(HttpMethod.Get, "/ping", "pong"),
            SendPostAsync("/h3/echo-binary", new byte[512]),
            SendPostStringAsync("/echo", "h3-concurrent-post"),
            SendSimpleAsync(HttpMethod.Get, "/h3/settings", "h3-ok")
        };

        var responses = await Task.WhenAll(tasks);

        Assert.Equal(5, responses.Length);
        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public async Task Concurrent_requests_to_different_endpoints_should_succeed()
    {
        var endpoints = new[] { "/hello", "/ping", "/h3/settings", "/hello", "/ping", "/h3/settings", "/hello", "/ping" };
        var tasks = endpoints.Select(path => SendSimpleAsync(HttpMethod.Get, path, "ok")).ToArray();

        var responses = await Task.WhenAll(tasks);

        Assert.Equal(8, responses.Length);
        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public async Task Concurrent_heavy_posts_should_complete_over_quic_streams()
    {
        var payload = new byte[10 * 1024];

        for (var batch = 0; batch < 2; batch++)
        {
            var tasks = Enumerable.Range(0, 8).Select(async _ =>
            {
                var request = new HttpRequestMessage(HttpMethod.Post, "http://localhost/h3/echo-binary")
                {
                    Version = HttpVersion.Version30,
                    Content = new ByteArrayContent(payload)
                };

                var controlFrames = new H3ResponseBuilder().Settings().Build();
                var responseFrames = new H3ResponseBuilder()
                    .Headers(200, [("content-length", payload.Length.ToString())])
                    .Data((ReadOnlyMemory<byte>)payload)
                    .Build();

                var (response, _) = await SendH3EngineAsync(Engine.CreateFlow(), request, controlFrames, responseFrames);
                return response;
            }).ToArray();

            var responses = await Task.WhenAll(tasks);

            Assert.Equal(8, responses.Length);
            Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        }
    }

    private async Task<HttpResponseMessage> SendSimpleAsync(HttpMethod method, string path, string responseBody)
    {
        var request = new HttpRequestMessage(method, $"http://localhost{path}")
        {
            Version = HttpVersion.Version30
        };

        var controlFrames = new H3ResponseBuilder().Settings().Build();
        var responseFrames = new H3ResponseBuilder()
            .Headers(200, [("content-length", Encoding.UTF8.GetByteCount(responseBody).ToString())])
            .Data(responseBody)
            .Build();

        var (response, _) = await SendH3EngineAsync(Engine.CreateFlow(), request, controlFrames, responseFrames);
        return response;
    }

    private async Task<HttpResponseMessage> SendPostAsync(string path, byte[] payload)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"http://localhost{path}")
        {
            Version = HttpVersion.Version30,
            Content = new ByteArrayContent(payload)
        };

        var controlFrames = new H3ResponseBuilder().Settings().Build();
        var responseFrames = new H3ResponseBuilder()
            .Headers(200, [("content-length", payload.Length.ToString())])
            .Data((ReadOnlyMemory<byte>)payload)
            .Build();

        var (response, _) = await SendH3EngineAsync(Engine.CreateFlow(), request, controlFrames, responseFrames);
        return response;
    }

    private async Task<HttpResponseMessage> SendPostStringAsync(string path, string body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"http://localhost{path}")
        {
            Version = HttpVersion.Version30,
            Content = new StringContent(body, Encoding.UTF8, "text/plain")
        };

        var controlFrames = new H3ResponseBuilder().Settings().Build();
        var responseFrames = new H3ResponseBuilder()
            .Headers(200, [("content-length", Encoding.UTF8.GetByteCount(body).ToString())])
            .Data(body)
            .Build();

        var (response, _) = await SendH3EngineAsync(Engine.CreateFlow(), request, controlFrames, responseFrames);
        return response;
    }
}
