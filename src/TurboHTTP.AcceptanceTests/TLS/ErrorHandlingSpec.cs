using System.Net;
using System.Text;
using Akka;
using Akka.Streams.Dsl;
using Servus.Akka.IO;
using TurboHTTP.Streams;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.AcceptanceTests.TLS;

public sealed class ErrorHandlingSpec : AcceptanceTestBase
{
    private static Http11Engine Engine =>
        new(new TurboClientOptions());

    private static byte[] BuildResponse(string body, HttpStatusCode status = HttpStatusCode.OK,
        string? extraHeaders = null)
    {
        var sb = new StringBuilder();
        sb.Append($"HTTP/1.1 {(int)status} {status}\r\n");
        sb.Append($"Content-Length: {Encoding.Latin1.GetByteCount(body)}\r\n");
        if (extraHeaders is not null)
        {
            sb.Append(extraHeaders);
        }

        sb.Append("\r\n");
        sb.Append(body);
        return Encoding.Latin1.GetBytes(sb.ToString());
    }

    private async Task<HttpResponseMessage> SendScriptedAsync(HttpRequestMessage request,
        Func<int, byte[], byte[]?> factory)
    {
        var fake = new ScriptedFakeConnectionStage(factory);
        var flow = Engine.CreateFlow().Join(Flow.FromGraph<IOutputItem, IInputItem, NotUsed>(fake));

        var tcs = new TaskCompletionSource<HttpResponseMessage>();
        _ = Source.Single(request)
            .Via(flow)
            .RunWith(Sink.ForEach<HttpResponseMessage>(res => tcs.TrySetResult(res)), Materializer);

        return await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-15.3")]
    public async Task ErrorHandling_should_complete_delay_route_after_server_wait_over_https()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://localhost/delay/500")
        {
            Version = HttpVersion.Version11
        };

        var response = await SendScriptedAsync(request, (_, _) => BuildResponse("delayed"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal("delayed", body);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-15.3")]
    public async Task ErrorHandling_should_abort_in_flight_request_on_timeout_cancellation_over_https()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://localhost/delay/10000")
        {
            Version = HttpVersion.Version11
        };

        var fake = new ScriptedFakeConnectionStage((_, _) => null);
        var flow = Engine.CreateFlow().Join(Flow.FromGraph<IOutputItem, IInputItem, NotUsed>(fake));

        var tcs = new TaskCompletionSource<HttpResponseMessage>();
        _ = Source.Single(request)
            .Via(flow)
            .RunWith(Sink.ForEach<HttpResponseMessage>(res => tcs.TrySetResult(res)), Materializer);

        await Assert.ThrowsAnyAsync<Exception>(async () =>
            await tcs.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6")]
    public async Task ErrorHandling_should_raise_exception_on_mid_response_connection_abort_over_https()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://localhost/edge/close-mid-response")
        {
            Version = HttpVersion.Version11
        };

        var raw = "HTTP/1.1 200 OK\r\nContent-Length: 10000\r\n\r\npartial";

        var fake = new ScriptedFakeConnectionStage((_, _) => Encoding.Latin1.GetBytes(raw));
        var flow = Engine.CreateFlow().Join(Flow.FromGraph<IOutputItem, IInputItem, NotUsed>(fake));

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
    [Trait("RFC", "RFC9110-8.4")]
    public async Task ErrorHandling_should_return_response_gracefully_with_unknown_content_encoding_over_https()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://localhost/edge/unknown-encoding")
        {
            Version = HttpVersion.Version11
        };

        var response = await SendScriptedAsync(request,
            (_, _) => BuildResponse("raw-body", extraHeaders: "Content-Encoding: x-custom-unknown\r\n"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6")]
    public async Task ErrorHandling_should_return_empty_for_empty_body_with_no_content_length_over_https()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://localhost/edge/empty-body")
        {
            Version = HttpVersion.Version11
        };

        var response = await SendScriptedAsync(request,
            (_, _) => Encoding.Latin1.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: 0\r\n\r\n"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal("", body);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6")]
    public async Task ErrorHandling_should_return_empty_body_for_content_length_zero_over_https()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://localhost/empty-cl")
        {
            Version = HttpVersion.Version11
        };

        var response = await SendScriptedAsync(request,
            (_, _) => Encoding.Latin1.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: 0\r\n\r\n"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal("", body);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-15.5")]
    public async Task ErrorHandling_should_return_4xx_status_code_400_over_https()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://localhost/status/400")
        {
            Version = HttpVersion.Version11
        };

        var response = await SendScriptedAsync(request,
            (_, _) => Encoding.Latin1.GetBytes("HTTP/1.1 400 Bad Request\r\nContent-Length: 0\r\n\r\n"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-15.5")]
    public async Task ErrorHandling_should_return_4xx_status_code_401_over_https()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://localhost/status/401")
        {
            Version = HttpVersion.Version11
        };

        var response = await SendScriptedAsync(request,
            (_, _) => Encoding.Latin1.GetBytes("HTTP/1.1 401 Unauthorized\r\nContent-Length: 0\r\n\r\n"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-15.5")]
    public async Task ErrorHandling_should_return_4xx_status_code_403_over_https()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://localhost/status/403")
        {
            Version = HttpVersion.Version11
        };

        var response = await SendScriptedAsync(request,
            (_, _) => Encoding.Latin1.GetBytes("HTTP/1.1 403 Forbidden\r\nContent-Length: 0\r\n\r\n"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-15.5")]
    public async Task ErrorHandling_should_return_4xx_status_code_404_over_https()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://localhost/status/404")
        {
            Version = HttpVersion.Version11
        };

        var response = await SendScriptedAsync(request,
            (_, _) => Encoding.Latin1.GetBytes("HTTP/1.1 404 Not Found\r\nContent-Length: 0\r\n\r\n"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-15.5")]
    public async Task ErrorHandling_should_return_4xx_status_code_429_over_https()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://localhost/status/429")
        {
            Version = HttpVersion.Version11
        };

        var response = await SendScriptedAsync(request,
            (_, _) => Encoding.Latin1.GetBytes("HTTP/1.1 429 Too Many Requests\r\nContent-Length: 0\r\n\r\n"));

        Assert.Equal((HttpStatusCode)429, response.StatusCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-15.6")]
    public async Task ErrorHandling_should_return_5xx_status_code_500_over_https()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://localhost/status/500")
        {
            Version = HttpVersion.Version11
        };

        var response = await SendScriptedAsync(request,
            (_, _) => Encoding.Latin1.GetBytes("HTTP/1.1 500 Internal Server Error\r\nContent-Length: 0\r\n\r\n"));

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-15.6")]
    public async Task ErrorHandling_should_return_5xx_status_code_502_over_https()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://localhost/status/502")
        {
            Version = HttpVersion.Version11
        };

        var response = await SendScriptedAsync(request,
            (_, _) => Encoding.Latin1.GetBytes("HTTP/1.1 502 Bad Gateway\r\nContent-Length: 0\r\n\r\n"));

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-15.6")]
    public async Task ErrorHandling_should_return_5xx_status_code_503_over_https()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://localhost/status/503")
        {
            Version = HttpVersion.Version11
        };

        var response = await SendScriptedAsync(request,
            (_, _) => Encoding.Latin1.GetBytes("HTTP/1.1 503 Service Unavailable\r\nContent-Length: 0\r\n\r\n"));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }
}