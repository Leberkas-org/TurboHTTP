using System.Net;
using System.Text;
using Akka;
using Akka.Streams.Dsl;
using TurboHTTP.Internal;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.AcceptanceTests.H10;

public sealed class EdgeCaseSpec : AcceptanceTestBase
{
    private static byte[] BuildResponse(byte[] body, HttpStatusCode status = HttpStatusCode.OK,
        string? extraHeaders = null)
    {
        var sb = new StringBuilder();
        sb.Append($"HTTP/1.0 {(int)status} {status}\r\n");
        sb.Append($"Content-Length: {body.Length}\r\n");
        if (extraHeaders is not null)
        {
            sb.Append(extraHeaders);
        }

        sb.Append("\r\n");

        var headerBytes = Encoding.Latin1.GetBytes(sb.ToString());
        var result = new byte[headerBytes.Length + body.Length];
        headerBytes.CopyTo(result, 0);
        body.CopyTo(result, headerBytes.Length);
        return result;
    }

    private static byte[] BuildResponse(string body, HttpStatusCode status = HttpStatusCode.OK,
        string? extraHeaders = null)
    {
        return BuildResponse(Encoding.Latin1.GetBytes(body), status, extraHeaders);
    }

    private async Task<HttpResponseMessage> SendScriptedAsync(HttpRequestMessage request,
        Func<int, byte[], byte[]?> factory)
    {
        var fake = new ScriptedFakeConnectionStage(factory);
        var flow = CreateHttp10Engine().CreateFlow().Join(Flow.FromGraph<IOutputItem, IInputItem, NotUsed>(fake));

        var tcs = new TaskCompletionSource<HttpResponseMessage>();
        _ = Source.Single(request)
            .Via(flow)
            .RunWith(Sink.ForEach<HttpResponseMessage>(res => tcs.TrySetResult(res)), Materializer);

        return await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-7.2")]
    public async Task EdgeCase_should_receive_large_256kb_body_via_connection_close()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/large/256")
        {
            Version = HttpVersion.Version10
        };

        var largeBody = new byte[256 * 1024];
        Array.Fill(largeBody, (byte)'A');

        var response = await SendScriptedAsync(request, (_, _) => BuildResponse(largeBody));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var bytes = await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal(256 * 1024, bytes.Length);
        Assert.All(bytes, b => Assert.Equal((byte)'A', b));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-7")]
    public async Task EdgeCase_should_echo_post_body_correctly()
    {
        const string payload = "hello from http10";
        var request = new HttpRequestMessage(HttpMethod.Post, "http://localhost/echo")
        {
            Version = HttpVersion.Version10,
            Content = new StringContent(payload, Encoding.UTF8, "text/plain")
        };

        var response = await SendScriptedAsync(request, (_, _) => BuildResponse(payload));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal(payload, body);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-6.1")]
    public async Task EdgeCase_should_return_status_code_200_correctly()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/status/200")
        {
            Version = HttpVersion.Version10
        };

        var response = await SendScriptedAsync(request, (_, _) => BuildResponse(""));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-6.1")]
    public async Task EdgeCase_should_return_status_code_404_correctly()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/status/404")
        {
            Version = HttpVersion.Version10
        };

        var raw = "HTTP/1.0 404 Not Found\r\nContent-Length: 0\r\n\r\n";

        var response = await SendScriptedAsync(request, (_, _) => Encoding.Latin1.GetBytes(raw));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-6.1")]
    public async Task EdgeCase_should_return_status_code_500_correctly()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/status/500")
        {
            Version = HttpVersion.Version10
        };

        var raw = "HTTP/1.0 500 Internal Server Error\r\nContent-Length: 0\r\n\r\n";

        var response = await SendScriptedAsync(request, (_, _) => Encoding.Latin1.GetBytes(raw));

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-5.2")]
    public async Task EdgeCase_should_echo_custom_headers_in_response()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/headers/echo")
        {
            Version = HttpVersion.Version10
        };
        request.Headers.Add("X-Custom-Test", "h10-value");
        request.Headers.Add("X-Another", "second");

        var response = await SendScriptedAsync(request,
            (_, _) => BuildResponse("", extraHeaders: "X-Custom-Test: h10-value\r\nX-Another: second\r\n"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.TryGetValues("X-Custom-Test", out var customValues));
        Assert.Equal("h10-value", string.Join("", customValues));
        Assert.True(response.Headers.TryGetValues("X-Another", out var anotherValues));
        Assert.Equal("second", string.Join("", anotherValues));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-7.2")]
    public async Task EdgeCase_should_complete_empty_body_response_without_hanging()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/edge/empty-body")
        {
            Version = HttpVersion.Version10
        };

        var response = await SendScriptedAsync(request,
            (_, _) => Encoding.Latin1.GetBytes("HTTP/1.0 200 OK\r\nContent-Length: 0\r\n\r\n"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal("", body);
    }
}