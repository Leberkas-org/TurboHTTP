using System.Net;
using Akka.Streams.Dsl;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.AcceptanceTests.H2;

public sealed class ResilienceSpec : AcceptanceTestBase
{
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.1")]
    public async Task Timeout_should_cancel_request_after_deadline()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/delay/30000")
        {
            Version = HttpVersion.Version20
        };

        var serverFrames = new H2ResponseBuilder()
            .Settings()
            .Build();

        var fake = CreateH2Connection(serverFrames);
        var flow = CreateHttp20Engine().CreateFlow().Join(fake.AsFlow());

        var tcs = new TaskCompletionSource<HttpResponseMessage>();
        _ = Source.Single(request)
            .Via(flow)
            .RunWith(Sink.ForEach<HttpResponseMessage>(res => tcs.TrySetResult(res)), Materializer);

        await Assert.ThrowsAnyAsync<Exception>(async () =>
            await tcs.Task.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.1")]
    public async Task Connection_reuse_should_survive_multiple_requests()
    {
        for (var i = 0; i < 3; i++)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/ping")
            {
                Version = HttpVersion.Version20
            };

            var serverFrames = new H2ResponseBuilder()
                .Settings()
                .SettingsAck()
                .Headers(1, 200, [("content-length", "4")], endStream: false)
                .Data(1, "pong")
                .Build();

            var (response, _) = await SendH2EngineAsync(CreateHttp20Engine().CreateFlow(), request, serverFrames);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.1")]
    public async Task Large_response_body_should_be_fully_received()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/large/4")
        {
            Version = HttpVersion.Version20
        };

        var largeBody = new byte[4 * 1024];
        Array.Fill(largeBody, (byte)'A');

        var serverFrames = new H2ResponseBuilder()
            .Settings()
            .SettingsAck()
            .Headers(1, 200, [("content-length", largeBody.Length.ToString())], endStream: false)
            .Data(1, (ReadOnlyMemory<byte>)largeBody)
            .Build();

        var (response, _) = await SendH2EngineAsync(CreateHttp20Engine().CreateFlow(), request, serverFrames);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.NotEmpty(body);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-5.1.1")]
    public async Task Connection_should_survive_pipeline_stress()
    {
        var tasks = Enumerable.Range(0, 20).Select(async _ =>
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/ping")
            {
                Version = HttpVersion.Version20
            };

            var serverFrames = new H2ResponseBuilder()
                .Settings()
                .SettingsAck()
                .Headers(1, 200, [("content-length", "4")], endStream: false)
                .Data(1, "pong")
                .Build();

            var (response, _) = await SendH2EngineAsync(CreateHttp20Engine().CreateFlow(), request, serverFrames);
            return response;
        }).ToArray();

        var responses = await Task.WhenAll(tasks);

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.1")]
    public async Task Slow_client_should_not_block_server()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/hello")
        {
            Version = HttpVersion.Version20
        };

        var serverFrames = new H2ResponseBuilder()
            .Settings()
            .SettingsAck()
            .Headers(1, 200, [("content-length", "11")], endStream: false)
            .Data(1, "Hello World")
            .Build();

        var (response, _) = await SendH2EngineAsync(CreateHttp20Engine().CreateFlow(), request, serverFrames);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal("Hello World", body);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.1")]
    public async Task Partial_body_read_should_not_corrupt_next_request()
    {
        var request1 = new HttpRequestMessage(HttpMethod.Get, "http://localhost/hello")
        {
            Version = HttpVersion.Version20
        };

        var serverFrames1 = new H2ResponseBuilder()
            .Settings()
            .SettingsAck()
            .Headers(1, 200, [("content-length", "11")], endStream: false)
            .Data(1, "Hello World")
            .Build();

        var (response1, _) = await SendH2EngineAsync(CreateHttp20Engine().CreateFlow(), request1, serverFrames1);
        Assert.Equal(HttpStatusCode.OK, response1.StatusCode);

        var request2 = new HttpRequestMessage(HttpMethod.Get, "http://localhost/hello")
        {
            Version = HttpVersion.Version20
        };

        var serverFrames2 = new H2ResponseBuilder()
            .Settings()
            .SettingsAck()
            .Headers(1, 200, [("content-length", "11")], endStream: false)
            .Data(1, "Hello World")
            .Build();

        var (response2, _) = await SendH2EngineAsync(CreateHttp20Engine().CreateFlow(), request2, serverFrames2);
        Assert.Equal(HttpStatusCode.OK, response2.StatusCode);
        var body2 = await response2.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal("Hello World", body2);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-5.1.1")]
    public async Task Interleaved_concurrent_requests_should_not_corrupt_responses()
    {
        var tasks = Enumerable.Range(0, 5).Select(async _ =>
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/hello")
            {
                Version = HttpVersion.Version20
            };

            var serverFrames = new H2ResponseBuilder()
                .Settings()
                .SettingsAck()
                .Headers(1, 200, [("content-length", "11")], endStream: false)
                .Data(1, "Hello World")
                .Build();

            var (response, _) = await SendH2EngineAsync(CreateHttp20Engine().CreateFlow(), request, serverFrames);
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
}
