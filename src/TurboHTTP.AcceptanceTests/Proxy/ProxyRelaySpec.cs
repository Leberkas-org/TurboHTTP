using System.Net;
using System.Text;
using Akka;
using Akka.Streams.Dsl;
using TurboHTTP.Internal;
using TurboHTTP.Streams;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.AcceptanceTests.Proxy;

public sealed class ProxyRelaySpec : AcceptanceTestBase
{
    private static Http11Engine Engine =>
        new(new Http1EngineOptions(16, 6, 3, 64 * 1024, 64, 1024 * 1024, TimeSpan.FromSeconds(2)));

    private static byte[] BuildResponse(string body, HttpStatusCode status = HttpStatusCode.OK)
    {
        return Encoding.Latin1.GetBytes(
            $"HTTP/1.1 {(int)status} {status}\r\nContent-Length: {Encoding.Latin1.GetByteCount(body)}\r\n\r\n{body}");
    }

    private async Task<HttpResponseMessage> SendScriptedAsync(
        HttpRequestMessage request,
        Func<int, byte[], byte[]?> responseFactory)
    {
        var fake = new ScriptedFakeConnectionStage(responseFactory);
        var flow = Engine.CreateFlow().Join(Flow.FromGraph<IOutputItem, IInputItem, NotUsed>(fake));

        var tcs = new TaskCompletionSource<HttpResponseMessage>();
        _ = Source.Single(request)
            .Via(flow)
            .RunWith(Sink.ForEach<HttpResponseMessage>(res => tcs.TrySetResult(res)), Materializer);

        return await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 5000)]
    public async Task Proxy_should_relay_plain_http_request()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/hello")
        {
            Version = HttpVersion.Version11
        };

        var response = await SendScriptedAsync(request,
            (_, _) => BuildResponse("Hello World"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal("Hello World", body);
    }

    [Fact(Timeout = 5000)]
    public async Task Proxy_should_relay_multiple_requests_on_same_connection()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/hello")
        {
            Version = HttpVersion.Version11
        };

        var response = await SendScriptedAsync(request,
            (_, _) => BuildResponse("Hello World"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact(Timeout = 5000)]
    public async Task Proxy_should_bypass_for_plain_http_when_use_proxy_false()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/hello")
        {
            Version = HttpVersion.Version11
        };

        var response = await SendScriptedAsync(request,
            (_, _) => BuildResponse("Hello World"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}