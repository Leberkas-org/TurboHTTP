using System.Net;
using Akka.Streams.Dsl;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.AcceptanceTests.H3;

public sealed class ErrorHandlingSpec : AcceptanceTestBase
{
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public async Task Status_4xx_should_be_returned_as_response_400()
    {
        await AssertStatusCodeAsync(400, HttpStatusCode.BadRequest);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public async Task Status_4xx_should_be_returned_as_response_401()
    {
        await AssertStatusCodeAsync(401, HttpStatusCode.Unauthorized);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public async Task Status_4xx_should_be_returned_as_response_403()
    {
        await AssertStatusCodeAsync(403, HttpStatusCode.Forbidden);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public async Task Status_4xx_should_be_returned_as_response_404()
    {
        await AssertStatusCodeAsync(404, HttpStatusCode.NotFound);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public async Task Status_4xx_should_be_returned_as_response_429()
    {
        await AssertStatusCodeAsync(429, (HttpStatusCode)429);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public async Task Status_5xx_should_be_returned_as_response_500()
    {
        await AssertStatusCodeAsync(500, HttpStatusCode.InternalServerError);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public async Task Status_5xx_should_be_returned_as_response_502()
    {
        await AssertStatusCodeAsync(502, HttpStatusCode.BadGateway);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public async Task Status_5xx_should_be_returned_as_response_503()
    {
        await AssertStatusCodeAsync(503, HttpStatusCode.ServiceUnavailable);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-8")]
    public async Task ErrorHandling_should_raise_exception_on_stream_abort()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/h3/abort")
        {
            Version = HttpVersion.Version30
        };

        var controlFrames = new H3ResponseBuilder().Settings().Build();
        var responseFrames = new H3ResponseBuilder()
            .GoAway(0)
            .Build();

        var fake = CreateH3Connection(controlFrames, responseFrames);
        var flow = CreateHttp30Engine().CreateFlow().Join(fake.AsFlow());

        var tcs = new TaskCompletionSource<HttpResponseMessage>();
        _ = Source.Single(request)
            .Via(flow)
            .RunWith(Sink.ForEach<HttpResponseMessage>(res => tcs.TrySetResult(res)), Materializer);

        await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            var response = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        });
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public async Task ErrorHandling_should_complete_delay_route_after_server_wait()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/h3/delay/500")
        {
            Version = HttpVersion.Version30
        };

        var controlFrames = new H3ResponseBuilder().Settings().Build();
        var responseFrames = new H3ResponseBuilder()
            .Headers(200, [("content-length", "7")])
            .Data("delayed")
            .Build();

        var (response, _) =
            await SendH3EngineAsync(CreateHttp30Engine().CreateFlow(), request, controlFrames, responseFrames);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal("delayed", body);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public async Task ErrorHandling_should_cancel_in_flight_request_on_timeout()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/h3/delay/10000")
        {
            Version = HttpVersion.Version30
        };

        var controlFrames = new H3ResponseBuilder().Settings().Build();

        var fake = CreateH3Connection(controlFrames);
        var flow = CreateHttp30Engine().CreateFlow().Join(fake.AsFlow());

        var tcs = new TaskCompletionSource<HttpResponseMessage>();
        _ = Source.Single(request)
            .Via(flow)
            .RunWith(Sink.ForEach<HttpResponseMessage>(res => tcs.TrySetResult(res)), Materializer);

        await Assert.ThrowsAnyAsync<Exception>(async () =>
            await tcs.Task.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-8")]
    public async Task ErrorHandling_should_raise_exception_on_mid_response_connection_abort()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/edge/close-mid-response")
        {
            Version = HttpVersion.Version30
        };

        var controlFrames = new H3ResponseBuilder().Settings().Build();
        var responseFrames = new H3ResponseBuilder()
            .GoAway(0)
            .Build();

        var fake = CreateH3Connection(controlFrames, responseFrames);
        var flow = CreateHttp30Engine().CreateFlow().Join(fake.AsFlow());

        var tcs = new TaskCompletionSource<HttpResponseMessage>();
        _ = Source.Single(request)
            .Via(flow)
            .RunWith(Sink.ForEach<HttpResponseMessage>(res => tcs.TrySetResult(res)), Materializer);

        await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            var response = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        });
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public async Task ErrorHandling_should_return_response_gracefully_with_unknown_content_encoding()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/edge/unknown-encoding")
        {
            Version = HttpVersion.Version30
        };

        var controlFrames = new H3ResponseBuilder().Settings().Build();
        var responseFrames = new H3ResponseBuilder()
            .Headers(200, [("content-encoding", "x-custom-unknown"), ("content-length", "4")])
            .Data("test")
            .Build();

        var (response, _) =
            await SendH3EngineAsync(CreateHttp30Engine().CreateFlow(), request, controlFrames, responseFrames);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public async Task ErrorHandling_should_access_custom_unknown_headers()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/unknown-headers")
        {
            Version = HttpVersion.Version30
        };

        var controlFrames = new H3ResponseBuilder().Settings().Build();
        var responseFrames = new H3ResponseBuilder()
            .Headers(200, [("x-unknown-foo", "bar"), ("x-unknown-bar", "baz"), ("content-length", "0")],
                endStream: true)
            .Build();

        var (response, _) =
            await SendH3EngineAsync(CreateHttp30Engine().CreateFlow(), request, controlFrames, responseFrames);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.TryGetValues("X-Unknown-Foo", out var fooValues));
        Assert.Equal("bar", string.Join("", fooValues));
        Assert.True(response.Headers.TryGetValues("X-Unknown-Bar", out var barValues));
        Assert.Equal("baz", string.Join("", barValues));
    }

    private async Task AssertStatusCodeAsync(int statusCode, HttpStatusCode expected)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"http://localhost/status/{statusCode}")
        {
            Version = HttpVersion.Version30
        };

        var controlFrames = new H3ResponseBuilder().Settings().Build();
        var responseFrames = new H3ResponseBuilder()
            .Headers(statusCode, endStream: true)
            .Build();

        var (response, _) =
            await SendH3EngineAsync(CreateHttp30Engine().CreateFlow(), request, controlFrames, responseFrames);
        Assert.Equal(expected, response.StatusCode);
    }
}
