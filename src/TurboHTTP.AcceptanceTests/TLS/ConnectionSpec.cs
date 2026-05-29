using TurboHTTP.Client;
using System.Net;
using System.Text;
using Akka.Streams.Dsl;
using TurboHTTP.Streams;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.AcceptanceTests.TLS;

public sealed class ConnectionSpec : AcceptanceTestBase
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

    private async Task<HttpResponseMessage> SendScriptedAsync(HttpRequestMessage request,
        Func<int, byte[], byte[]?> factory)
    {
        var fake = CreateScriptedConnection(factory);
        var flow = Engine.CreateFlow().Join(fake.AsFlow());

        var tcs = new TaskCompletionSource<HttpResponseMessage>();
        _ = Source.Single(request)
            .Via(flow)
            .RunWith(Sink.ForEach<HttpResponseMessage>(res => tcs.TrySetResult(res)), Materializer);

        return await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9.3")]
    public async Task Connection_should_allow_sequential_requests_with_keep_alive()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://localhost/conn/keep-alive")
        {
            Version = HttpVersion.Version11
        };

        var response = await SendScriptedAsync(request,
            (_, _) => BuildResponse("keep-alive"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal("keep-alive", body);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9.3")]
    public async Task Connection_should_have_close_header_in_response()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://localhost/conn/close")
        {
            Version = HttpVersion.Version11
        };

        var response = await SendScriptedAsync(request,
            (_, _) => BuildResponse("closing", extraHeaders: "Connection: close\r\n"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal("closing", body);

        Assert.True(
            response.Headers.Connection.Contains("close"),
            "Response should contain Connection: close header");
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9.3")]
    public async Task Connection_should_default_to_keep_alive_without_connection_header()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://localhost/conn/default")
        {
            Version = HttpVersion.Version11
        };

        var response = await SendScriptedAsync(request,
            (_, _) => BuildResponse("default"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal("default", body);
    }

    [Fact(Timeout = 10000)]
    [Trait("RFC", "RFC9110-7.8")]
    public async Task Connection_101_switching_protocols_must_not_be_reusable_for_http()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://localhost/conn/upgrade-101")
        {
            Version = HttpVersion.Version11
        };

        var fake = CreateScriptedConnection((_, _) =>
            Encoding.Latin1.GetBytes(
                "HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\n\r\n"));
        var flow = Engine.CreateFlow().Join(fake.AsFlow());

        var tcs = new TaskCompletionSource<HttpResponseMessage>();
        _ = Source.Single(request)
            .Via(flow)
            .RunWith(Sink.ForEach<HttpResponseMessage>(res => tcs.TrySetResult(res)), Materializer);

        await Assert.ThrowsAnyAsync<Exception>(async () =>
            await tcs.Task.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9.3")]
    public async Task Connection_should_prove_reuse_across_different_endpoints()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://localhost/conn/keep-alive")
        {
            Version = HttpVersion.Version11
        };

        var response = await SendScriptedAsync(request, (_, _) => BuildResponse("keep-alive"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal("keep-alive", body);
    }
}

