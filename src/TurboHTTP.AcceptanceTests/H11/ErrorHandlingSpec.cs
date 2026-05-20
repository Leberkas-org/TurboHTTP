using TurboHTTP.Client;
using System.Net;
using System.Text;
using Akka.Streams.Dsl;
using TurboHTTP.Streams;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.AcceptanceTests.H11;

public sealed class ErrorHandlingSpec : ClientAcceptanceTestBase
{
    private static Http11ClientEngine Engine =>
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

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-15.3")]
    public async Task ErrorHandling_should_complete_delay_route_after_server_wait()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://fake.test/delay/500")
        {
            Version = HttpVersion.Version11
        };

        var response = await SendClientAsync(HttpVersion.Version11, request, (_, _) => BuildResponse("delayed"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal("delayed", body);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-15.3")]
    public async Task ErrorHandling_should_abort_in_flight_request_on_timeout_cancellation()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://fake.test/delay/10000")
        {
            Version = HttpVersion.Version11
        };

        var fake = CreateScriptedConnection((_, _) => null);
        var flow = Engine.CreateFlow().Join(fake.AsFlow());

        var tcs = new TaskCompletionSource<HttpResponseMessage>();
        _ = Source.Single(request)
            .Via(flow)
            .RunWith(Sink.ForEach<HttpResponseMessage>(res => tcs.TrySetResult(res)), Materializer);

        await Assert.ThrowsAnyAsync<Exception>(async () =>
            await tcs.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6")]
    public async Task ErrorHandling_should_raise_exception_on_mid_response_connection_abort()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://fake.test/edge/close-mid-response")
        {
            Version = HttpVersion.Version11
        };

        // Content-Length says 10000, but we only send 7 bytes then abort
        var raw = "HTTP/1.1 200 OK\r\nContent-Length: 10000\r\n\r\npartial";

        var fake = CreateScriptedConnectionWithClose((_, _) => Encoding.Latin1.GetBytes(raw));
        var flow = Engine.CreateFlow().Join(fake.AsFlow());

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
    [Trait("RFC", "RFC9110-6.5")]
    public async Task ErrorHandling_should_receive_large_response_headers_1kb()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://fake.test/edge/large-header/1")
        {
            Version = HttpVersion.Version11
        };

        var headerValue = new string('X', 1 * 1024);

        var response = await SendClientAsync(HttpVersion.Version11, request,
            (_, _) => BuildResponse("", extraHeaders: $"X-Large-Header: {headerValue}\r\n"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.TryGetValues("X-Large-Header", out var values));
        var val = string.Join("", values);
        Assert.Equal(1 * 1024, val.Length);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-6.5")]
    public async Task ErrorHandling_should_receive_large_response_headers_4kb()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://fake.test/edge/large-header/4")
        {
            Version = HttpVersion.Version11
        };

        var headerValue = new string('X', 4 * 1024);

        var response = await SendClientAsync(HttpVersion.Version11, request,
            (_, _) => BuildResponse("", extraHeaders: $"X-Large-Header: {headerValue}\r\n"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.TryGetValues("X-Large-Header", out var values));
        var val = string.Join("", values);
        Assert.Equal(4 * 1024, val.Length);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-8.4")]
    public async Task ErrorHandling_should_return_response_gracefully_with_unknown_content_encoding()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://fake.test/edge/unknown-encoding")
        {
            Version = HttpVersion.Version11
        };

        var response = await SendClientAsync(HttpVersion.Version11, request,
            (_, _) => BuildResponse("raw-body", extraHeaders: "Content-Encoding: x-custom-unknown\r\n"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6")]
    public async Task ErrorHandling_should_return_empty_for_empty_body_with_no_content_length()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://fake.test/edge/empty-body")
        {
            Version = HttpVersion.Version11
        };

        var response = await SendClientAsync(HttpVersion.Version11, request,
            (_, _) => Encoding.Latin1.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: 0\r\n\r\n"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal("", body);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6")]
    public async Task ErrorHandling_should_return_empty_body_for_content_length_zero()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://fake.test/empty-cl")
        {
            Version = HttpVersion.Version11
        };

        var response = await SendClientAsync(HttpVersion.Version11, request,
            (_, _) => Encoding.Latin1.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: 0\r\n\r\n"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal("", body);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-15.5")]
    public async Task ErrorHandling_should_return_4xx_status_code_400()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://fake.test/status/400")
        {
            Version = HttpVersion.Version11
        };

        var response = await SendClientAsync(HttpVersion.Version11, request,
            (_, _) => Encoding.Latin1.GetBytes("HTTP/1.1 400 Bad Request\r\nContent-Length: 0\r\n\r\n"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-15.5")]
    public async Task ErrorHandling_should_return_4xx_status_code_401()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://fake.test/status/401")
        {
            Version = HttpVersion.Version11
        };

        var response = await SendClientAsync(HttpVersion.Version11, request,
            (_, _) => Encoding.Latin1.GetBytes("HTTP/1.1 401 Unauthorized\r\nContent-Length: 0\r\n\r\n"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-15.5")]
    public async Task ErrorHandling_should_return_4xx_status_code_403()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://fake.test/status/403")
        {
            Version = HttpVersion.Version11
        };

        var response = await SendClientAsync(HttpVersion.Version11, request,
            (_, _) => Encoding.Latin1.GetBytes("HTTP/1.1 403 Forbidden\r\nContent-Length: 0\r\n\r\n"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-15.5")]
    public async Task ErrorHandling_should_return_4xx_status_code_404()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://fake.test/status/404")
        {
            Version = HttpVersion.Version11
        };

        var response = await SendClientAsync(HttpVersion.Version11, request,
            (_, _) => Encoding.Latin1.GetBytes("HTTP/1.1 404 Not Found\r\nContent-Length: 0\r\n\r\n"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-15.5")]
    public async Task ErrorHandling_should_return_4xx_status_code_429()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://fake.test/status/429")
        {
            Version = HttpVersion.Version11
        };

        var response = await SendClientAsync(HttpVersion.Version11, request,
            (_, _) => Encoding.Latin1.GetBytes("HTTP/1.1 429 Too Many Requests\r\nContent-Length: 0\r\n\r\n"));

        Assert.Equal((HttpStatusCode)429, response.StatusCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-15.6")]
    public async Task ErrorHandling_should_return_5xx_status_code_500()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://fake.test/status/500")
        {
            Version = HttpVersion.Version11
        };

        var response = await SendClientAsync(HttpVersion.Version11, request,
            (_, _) => Encoding.Latin1.GetBytes("HTTP/1.1 500 Internal Server Error\r\nContent-Length: 0\r\n\r\n"));

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-15.6")]
    public async Task ErrorHandling_should_return_5xx_status_code_502()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://fake.test/status/502")
        {
            Version = HttpVersion.Version11
        };

        var response = await SendClientAsync(HttpVersion.Version11, request,
            (_, _) => Encoding.Latin1.GetBytes("HTTP/1.1 502 Bad Gateway\r\nContent-Length: 0\r\n\r\n"));

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-15.6")]
    public async Task ErrorHandling_should_return_5xx_status_code_503()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://fake.test/status/503")
        {
            Version = HttpVersion.Version11
        };

        var response = await SendClientAsync(HttpVersion.Version11, request,
            (_, _) => Encoding.Latin1.GetBytes("HTTP/1.1 503 Service Unavailable\r\nContent-Length: 0\r\n\r\n"));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-6.5")]
    public async Task ErrorHandling_should_access_custom_unknown_headers()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://fake.test/unknown-headers")
        {
            Version = HttpVersion.Version11
        };

        var response = await SendClientAsync(HttpVersion.Version11, request,
            (_, _) => BuildResponse("", extraHeaders: "X-Unknown-Foo: bar\r\nX-Unknown-Bar: baz\r\n"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.TryGetValues("X-Unknown-Foo", out var fooValues));
        Assert.Equal("bar", string.Join("", fooValues));
        Assert.True(response.Headers.TryGetValues("X-Unknown-Bar", out var barValues));
        Assert.Equal("baz", string.Join("", barValues));
    }
}

