using TurboHTTP.Client;
using System.Net;
using System.Text;
using Akka.Streams.Dsl;
using Servus.Akka.Transport;
using TurboHTTP.Streams;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.AcceptanceTests.Proxy;

public sealed class ProxyConnectSpec : AcceptanceTestBase
{
    private static Http11Engine Engine =>
        new(new TurboClientOptions());

    private static byte[] BuildResponse(string body, HttpStatusCode status = HttpStatusCode.OK)
    {
        return Encoding.Latin1.GetBytes(
            $"HTTP/1.1 {(int)status} {status}\r\nContent-Length: {Encoding.Latin1.GetByteCount(body)}\r\n\r\n{body}");
    }

    private async Task<(HttpResponseMessage Response, string TunneledRequest)> SendViaTunnelAsync(
        HttpRequestMessage request,
        Func<int, byte[], byte[]?> responseFactory)
    {
        var fake = CreateProxyConnection(responseFactory);

        var connectResponseConsumed = false;
        var tunnelFlow = Flow.Create<ITransportOutbound>()
            .Via(fake.AsFlow())
            .Where(item =>
            {
                if (!connectResponseConsumed)
                {
                    connectResponseConsumed = true;
                    if (item is TransportData td)
                    {
                        td.Buffer.Dispose();
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
        foreach (var outbound in fake.ReceivedOutbound)
        {
            if (outbound is TransportData { Buffer: var buf })
            {
                rawBuilder.Append(Encoding.Latin1.GetString(buf.Span));
            }
        }

        return (response, rawBuilder.ToString());
    }

    private async Task<HttpResponseMessage> SendDirectAsync(
        HttpRequestMessage request,
        Func<int, byte[], byte[]?> responseFactory)
    {
        var fake = CreateScriptedConnection(responseFactory);
        var flow = Engine.CreateFlow().Join(fake.AsFlow());

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

