using System.Net;
using System.Text;
using Akka.Streams.Dsl;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.AcceptanceTests.H10;

public sealed class ConnectionSpec : AcceptanceTestBase
{
    private static byte[] BuildResponse(string body, HttpStatusCode status = HttpStatusCode.OK,
        string? extraHeaders = null)
    {
        var sb = new StringBuilder();
        sb.Append($"HTTP/1.0 {(int)status} {status}\r\n");
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
        var fake = CreateScriptedConnection(factory);
        var flow = CreateHttp10Engine().CreateFlow().Join(fake.AsFlow());

        var tcs = new TaskCompletionSource<HttpResponseMessage>();
        _ = Source.Single(request)
            .Via(flow)
            .RunWith(Sink.ForEach<HttpResponseMessage>(res => tcs.TrySetResult(res)), Materializer);

        return await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-5")]
    public async Task Connection_should_close_after_single_request_by_default()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/conn/default")
        {
            Version = HttpVersion.Version10
        };

        var response = await SendScriptedAsync(request, (_, _) => BuildResponse("default"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal("default", body);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-5")]
    public async Task Connection_should_allow_sequential_requests_with_keep_alive_opt_in()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/conn/keep-alive")
        {
            Version = HttpVersion.Version10
        };

        var response = await SendScriptedAsync(request,
            (_, _) => BuildResponse("keep-alive", extraHeaders: "Connection: Keep-Alive\r\n"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal("keep-alive", body);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-4.1")]
    public async Task Connection_should_return_expected_body_for_simple_get()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/hello")
        {
            Version = HttpVersion.Version10
        };

        var response = await SendScriptedAsync(request, (_, _) => BuildResponse("Hello World"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal("Hello World", body);
    }
}
