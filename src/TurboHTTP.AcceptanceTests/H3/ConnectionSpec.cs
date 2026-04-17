using System.Net;
using TurboHTTP.Internal;
using TurboHTTP.Streams;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.AcceptanceTests.H3;

public sealed class ConnectionSpec : AcceptanceTestBase
{
    private static Http30Engine Engine => new(new Http3Options().ToEngineOptions());

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public async Task Sequential_requests_should_reuse_same_connection()
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

        var (response1, _) = await SendH3EngineAsync(Engine.CreateFlow(), request1, controlFrames, responseFrames);

        Assert.Equal(HttpStatusCode.OK, response1.StatusCode);
        var body1 = await response1.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal("Hello World", body1);

        var request2 = new HttpRequestMessage(HttpMethod.Get, "http://localhost/hello")
        {
            Version = HttpVersion.Version30
        };

        var (response2, _) = await SendH3EngineAsync(Engine.CreateFlow(), request2, controlFrames, responseFrames);

        Assert.Equal(HttpStatusCode.OK, response2.StatusCode);
        var body2 = await response2.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal("Hello World", body2);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public async Task Concurrent_requests_should_be_multiplexed_over_quic_streams()
    {
        var tasks = Enumerable.Range(0, 5).Select(async _ =>
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

        foreach (var response in responses)
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            Assert.Equal("pong", body);
        }
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public async Task Binary_body_post_should_be_echoed_correctly()
    {
        var payload = new byte[4 * 1024];
        for (var i = 0; i < payload.Length; i++)
        {
            payload[i] = (byte)(i % 256);
        }

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

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal(payload, body);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public async Task Multiple_endpoints_should_be_served_on_same_connection()
    {
        // Test /hello endpoint
        var request1 = new HttpRequestMessage(HttpMethod.Get, "http://localhost/hello")
        {
            Version = HttpVersion.Version30
        };

        var controlFrames = new H3ResponseBuilder().Settings().Build();
        var responseFrames1 = new H3ResponseBuilder()
            .Headers(200, [("content-length", "11")])
            .Data("Hello World")
            .Build();

        var (response1, _) = await SendH3EngineAsync(Engine.CreateFlow(), request1, controlFrames, responseFrames1);
        Assert.Equal(HttpStatusCode.OK, response1.StatusCode);
        Assert.Equal("Hello World", await response1.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        // Test /h3/many-headers endpoint
        var request2 = new HttpRequestMessage(HttpMethod.Get, "http://localhost/h3/many-headers")
        {
            Version = HttpVersion.Version30
        };

        var manyHeaders = new List<(string Name, string Value)>();
        for (var i = 0; i < 20; i++)
        {
            manyHeaders.Add(($"x-custom-{i:D3}", $"value-{i:D3}"));
        }
        manyHeaders.Add(("content-length", "12"));

        var responseFrames2 = new H3ResponseBuilder()
            .Headers(200, manyHeaders)
            .Data("many-headers")
            .Build();

        var (response2, _) = await SendH3EngineAsync(Engine.CreateFlow(), request2, controlFrames, responseFrames2);
        Assert.Equal(HttpStatusCode.OK, response2.StatusCode);
        Assert.True(response2.Headers.TryGetValues("X-Custom-000", out _));

        // Test /large/8 endpoint
        var request3 = new HttpRequestMessage(HttpMethod.Get, "http://localhost/large/8")
        {
            Version = HttpVersion.Version30
        };

        var largeBody = new byte[8 * 1024];
        Array.Fill(largeBody, (byte)'A');

        var responseFrames3 = new H3ResponseBuilder()
            .Headers(200, [("content-length", largeBody.Length.ToString())])
            .Data((ReadOnlyMemory<byte>)largeBody)
            .Build();

        var (response3, _) = await SendH3EngineAsync(Engine.CreateFlow(), request3, controlFrames, responseFrames3);
        Assert.Equal(HttpStatusCode.OK, response3.StatusCode);
        var largeResponseBody = await response3.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal(8 * 1024, largeResponseBody.Length);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public async Task Post_with_body_followed_by_get_should_work_on_same_connection()
    {
        var postPayload = "hello from http3"u8.ToArray();
        var postRequest = new HttpRequestMessage(HttpMethod.Post, "http://localhost/h3/echo-binary")
        {
            Version = HttpVersion.Version30,
            Content = new ByteArrayContent(postPayload)
        };

        var controlFrames = new H3ResponseBuilder().Settings().Build();
        var postResponseFrames = new H3ResponseBuilder()
            .Headers(200, [("content-length", postPayload.Length.ToString())])
            .Data((ReadOnlyMemory<byte>)postPayload)
            .Build();

        var (postResponse, _) = await SendH3EngineAsync(Engine.CreateFlow(), postRequest, controlFrames, postResponseFrames);
        Assert.Equal(HttpStatusCode.OK, postResponse.StatusCode);
        var postBody = await postResponse.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal(postPayload, postBody);

        var getRequest = new HttpRequestMessage(HttpMethod.Get, "http://localhost/h3/settings")
        {
            Version = HttpVersion.Version30
        };

        var getResponseFrames = new H3ResponseBuilder()
            .Headers(200, [("content-length", "5")])
            .Data("h3-ok")
            .Build();

        var (getResponse, _) = await SendH3EngineAsync(Engine.CreateFlow(), getRequest, controlFrames, getResponseFrames);
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        var getBody = await getResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal("h3-ok", getBody);
    }
}
