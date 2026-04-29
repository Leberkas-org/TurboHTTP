using System.Net;
using System.Text;
using Akka;
using Akka.Streams.Dsl;
using Servus.Akka.Transport;
using TurboHTTP.Protocol.Semantics;
using TurboHTTP.Streams.Stages.Features;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.AcceptanceTests.H10;

public sealed class ExpectContinueSpec : AcceptanceTestBase
{
    private static BidiFlow<HttpRequestMessage, ITransportOutbound, ITransportInbound, HttpResponseMessage, NotUsed>
        CreateExpectContinueEngine()
    {
        var stage = new ExpectContinueBidiStage(Expect100Policy.Default);
        return BidiFlow.FromGraph(stage).Atop(CreateHttp10Engine().CreateFlow());
    }

    private static byte[] BuildResponse(string body, HttpStatusCode status = HttpStatusCode.OK)
    {
        var sb = new StringBuilder();
        sb.Append($"HTTP/1.0 {(int)status} {status}\r\n");
        sb.Append($"Content-Length: {Encoding.UTF8.GetByteCount(body)}\r\n");
        sb.Append("\r\n");
        sb.Append(body);
        return Encoding.Latin1.GetBytes(sb.ToString());
    }

    private async Task<HttpResponseMessage> SendExpectAsync(HttpRequestMessage request,
        Func<int, byte[], byte[]?> factory)
    {
        var fake = new ScriptedFakeConnectionStage(factory);
        var flow = CreateExpectContinueEngine().Join(Flow.FromGraph<ITransportOutbound, ITransportInbound, NotUsed>(fake));

        var tcs = new TaskCompletionSource<HttpResponseMessage>();
        _ = Source.Single(request)
            .Via(flow)
            .RunWith(Sink.ForEach<HttpResponseMessage>(res => tcs.TrySetResult(res)), Materializer);

        return await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-10.1.1")]
    public async Task ExpectContinue_should_send_small_body_without_expect_header()
    {
        const string body = "hello";
        var request = new HttpRequestMessage(HttpMethod.Post, "http://localhost/expect/echo")
        {
            Version = HttpVersion.Version10,
            Content = new StringContent(body, Encoding.UTF8, "text/plain")
        };

        var response = await SendExpectAsync(request, (_, _) => BuildResponse(body));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var responseBody = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal(body, responseBody);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-10.1.1")]
    public async Task ExpectContinue_should_send_large_body_without_100_continue()
    {
        var body = new string('x', 2048);
        var request = new HttpRequestMessage(HttpMethod.Post, "http://localhost/expect/large")
        {
            Version = HttpVersion.Version10,
            Content = new StringContent(body, Encoding.UTF8, "text/plain")
        };

        var response = await SendExpectAsync(request, (_, _) => BuildResponse(body));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var responseBody = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal(body, responseBody);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-10.1.1")]
    public async Task ExpectContinue_should_return_417_on_server_rejection()
    {
        var body = new string('x', 2048);
        var request = new HttpRequestMessage(HttpMethod.Post, "http://localhost/expect/reject")
        {
            Version = HttpVersion.Version10,
            Content = new StringContent(body, Encoding.UTF8, "text/plain")
        };

        var response = await SendExpectAsync(request,
            (_, _) => Encoding.Latin1.GetBytes("HTTP/1.0 417 Expectation Failed\r\nContent-Length: 0\r\n\r\n"));

        Assert.Equal(HttpStatusCode.ExpectationFailed, response.StatusCode);
    }
}

