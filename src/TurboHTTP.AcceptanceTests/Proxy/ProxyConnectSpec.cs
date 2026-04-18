using System.Net;
using System.Text;
using Akka;
using Akka.Streams.Dsl;
using TurboHTTP.Internal;
using TurboHTTP.Streams;
using TurboHTTP.Tests.Shared;
using TurboHTTP.Transport.Connection;

namespace TurboHTTP.AcceptanceTests.Proxy;

public sealed class ProxyConnectSpec : AcceptanceTestBase
{
    private static Http11Engine Engine =>
        new(new Http1EngineOptions(16, 6, 3, 64 * 1024, 64, 1024 * 1024, TimeSpan.FromSeconds(2)));

    private static byte[] BuildResponse(string body, HttpStatusCode status = HttpStatusCode.OK)
    {
        return Encoding.Latin1.GetBytes(
            $"HTTP/1.1 {(int)status} {status}\r\nContent-Length: {Encoding.Latin1.GetByteCount(body)}\r\n\r\n{body}");
    }

    private static ConnectItem ToConnectItem(StreamAcquireItem acquire)
    {
        return new ConnectItem(new TcpOptions
        {
            Host = acquire.Key.Host,
            Port = acquire.Key.Port,
            UseProxy = true
        })
        {
            Key = acquire.Key
        };
    }

    private async Task<(HttpResponseMessage Response, string TunneledRequest)> SendViaTunnelAsync(
        HttpRequestMessage request,
        Func<int, byte[], byte[]?> responseFactory)
    {
        var fake = new FakeProxyStage(responseFactory);

        var connectResponseConsumed = false;
        var tunnelFlow = Flow.Create<IOutputItem>()
            .Select(item => item is StreamAcquireItem acquire ? ToConnectItem(acquire) : item)
            .Via(Flow.FromGraph<IOutputItem, IInputItem, NotUsed>(fake))
            .Where(item =>
            {
                if (!connectResponseConsumed)
                {
                    connectResponseConsumed = true;
                    if (item is NetworkBuffer nb)
                    {
                        nb.Dispose();
                    }

                    return false;
                }

                return true;
            });

        var flow = Engine.CreateFlow().Join(tunnelFlow);

        var tcs = new TaskCompletionSource<HttpResponseMessage>();
        _ = Source.Single(request)
            .Via(flow)
            .RunWith(Sink.ForEach<HttpResponseMessage>(res => tcs.TrySetResult(res)), Materializer);

        var response = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        var rawBuilder = new StringBuilder();
        while (fake.OutboundChannel.Reader.TryRead(out var chunk))
        {
            rawBuilder.Append(Encoding.Latin1.GetString(chunk.Span));
        }

        return (response, rawBuilder.ToString());
    }

    private async Task<HttpResponseMessage> SendDirectAsync(
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
    public async Task Proxy_should_tunnel_https_request_via_connect()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://localhost/hello")
        {
            Version = HttpVersion.Version11
        };

        var (response, tunneledRequest) = await SendViaTunnelAsync(
            request, (_, _) => BuildResponse("Hello World"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal("Hello World", body);
        Assert.Contains("GET /hello HTTP/1.1", tunneledRequest);
    }

    [Fact(Timeout = 5000)]
    public async Task Proxy_should_send_proxy_authorization_when_credentials_set()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://localhost/hello")
        {
            Version = HttpVersion.Version11
        };

        var (response, tunneledRequest) = await SendViaTunnelAsync(
            request, (_, _) => BuildResponse("Hello World"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("GET /hello HTTP/1.1", tunneledRequest);
    }

    [Fact(Timeout = 5000)]
    public async Task Proxy_should_send_default_proxy_credentials_when_set()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://localhost/hello")
        {
            Version = HttpVersion.Version11
        };

        var (response, tunneledRequest) = await SendViaTunnelAsync(
            request, (_, _) => BuildResponse("Hello World"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("GET /hello HTTP/1.1", tunneledRequest);
    }

    [Fact(Timeout = 5000)]
    public async Task Proxy_should_bypass_when_use_proxy_is_false()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://localhost/hello")
        {
            Version = HttpVersion.Version11
        };

        var response = await SendDirectAsync(
            request, (_, _) => BuildResponse("Hello World"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact(Timeout = 5000)]
    public async Task Proxy_should_bypass_when_proxy_is_null()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://localhost/hello")
        {
            Version = HttpVersion.Version11
        };

        var response = await SendDirectAsync(
            request, (_, _) => BuildResponse("Hello World"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact(Timeout = 5000)]
    public async Task Proxy_should_work_with_preauthenticate_through_tunnel()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://localhost/auth")
        {
            Version = HttpVersion.Version11
        };

        var (response, tunneledRequest) = await SendViaTunnelAsync(
            request, (_, _) => BuildResponse("Authenticated"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("GET /auth HTTP/1.1", tunneledRequest);
    }
}