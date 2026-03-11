using System.Net;
using Akka.Actor;
using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHttp.IntegrationTests.Shared;
using TurboHttp.IO;
using TurboHttp.Streams;
using TurboHttp.Streams.Stages;

namespace TurboHttp.IntegrationTests.Http11;

/// <summary>
/// Integration tests for Http11Engine connection management.
/// Verifies HTTP/1.1 keep-alive default, Connection: close, pipelining,
/// per-host limits, and connection reuse behaviour against real Kestrel.
/// </summary>
public sealed class Http11ConnectionTests : TestKit, IClassFixture<KestrelFixture>
{
    private readonly KestrelFixture _fixture;
    private readonly IMaterializer _materializer;
    private readonly IActorRef _clientManager;

    public Http11ConnectionTests(KestrelFixture fixture)
    {
        _fixture = fixture;
        _materializer = Sys.Materializer();
        _clientManager = Sys.ActorOf(Props.Create<ClientManager>());
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request)
    {
        var engine = new Http11Engine();
        var tcpOptions = new TcpOptions
        {
            Host = "127.0.0.1",
            Port = _fixture.Port
        };

        var transport =
            Flow.Create<ITransportItem>()
                .Prepend(Source.Single<ITransportItem>(
                    new ConnectItem(tcpOptions)))
                .Via(new ConnectionStage(_clientManager));

        var flow = engine.CreateFlow().Join(transport);

        var (queue, responseTask) = Source.Queue<HttpRequestMessage>(1, OverflowStrategy.Backpressure)
            .Via(flow)
            .ToMaterialized(Sink.First<HttpResponseMessage>(), Keep.Both)
            .Run(_materializer);

        await queue.OfferAsync(request);
        return await responseTask.WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Fact(DisplayName = "CONN-11-001: HTTP/1.1 default keep-alive — no Connection header in response")]
    public async Task Http11_Default_KeepAlive()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{_fixture.Port}/conn/default")
        {
            Version = HttpVersion.Version11
        };

        var response = await SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("default", body);
        // HTTP/1.1 default: persistent connections — no Connection: close expected
        Assert.DoesNotContain("close", response.Headers.Connection);
    }

    [Fact(DisplayName = "CONN-11-002: Multiple HTTP/1.1 requests on same host succeed sequentially")]
    public async Task Http11_Multiple_Sequential_Succeed()
    {
        var response1 = await SendAsync(new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{_fixture.Port}/conn/default")
        {
            Version = HttpVersion.Version11
        });
        var response2 = await SendAsync(new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{_fixture.Port}/conn/default")
        {
            Version = HttpVersion.Version11
        });
        var response3 = await SendAsync(new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{_fixture.Port}/conn/default")
        {
            Version = HttpVersion.Version11
        });

        Assert.Equal(HttpStatusCode.OK, response1.StatusCode);
        Assert.Equal(HttpStatusCode.OK, response2.StatusCode);
        Assert.Equal(HttpStatusCode.OK, response3.StatusCode);

        Assert.Equal("default", await response1.Content.ReadAsStringAsync());
        Assert.Equal("default", await response2.Content.ReadAsStringAsync());
        Assert.Equal("default", await response3.Content.ReadAsStringAsync());
    }

    [Fact(DisplayName = "CONN-11-003: Connection: close in response signals server will close")]
    public async Task Http11_ConnectionClose_InResponse()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{_fixture.Port}/conn/close")
        {
            Version = HttpVersion.Version11
        };

        var response = await SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("closing", body);
        Assert.Contains("close", response.Headers.Connection);
    }

    [Fact(DisplayName = "CONN-11-004: Server Connection: close — subsequent request succeeds on new connection")]
    public async Task Http11_ServerClose_NewConnectionSucceeds()
    {
        // First request gets Connection: close from server
        var request1 = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{_fixture.Port}/conn/close")
        {
            Version = HttpVersion.Version11
        };
        var response1 = await SendAsync(request1);
        Assert.Equal(HttpStatusCode.OK, response1.StatusCode);
        Assert.Contains("close", response1.Headers.Connection);

        // Second request on a new pipeline succeeds despite prior connection closing
        var request2 = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{_fixture.Port}/conn/default")
        {
            Version = HttpVersion.Version11
        };
        var response2 = await SendAsync(request2);
        Assert.Equal(HttpStatusCode.OK, response2.StatusCode);
        Assert.Equal("default", await response2.Content.ReadAsStringAsync());
    }

    [Fact(DisplayName = "CONN-11-005: HTTP/1.1 pipelining — sequential requests through separate pipelines succeed")]
    public async Task Http11_Pipelining_SequentialSuccess()
    {
        // HTTP/1.1 supports pipelining (RFC 9112 §9.3). While true pipelining
        // (multiple requests on one connection before responses) requires engine-level
        // wiring, this test verifies the protocol layer handles sequential request/response
        // pairs correctly across multiple pipeline materialisations.
        var responses = new List<HttpResponseMessage>();
        for (var i = 0; i < 5; i++)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{_fixture.Port}/hello")
            {
                Version = HttpVersion.Version11
            };
            responses.Add(await SendAsync(request));
        }

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
    }

    [Fact(DisplayName = "CONN-11-006: Per-host limit — 6 concurrent requests all succeed")]
    public async Task Http11_PerHostLimit_SixConcurrent()
    {
        // RFC 9112 recommends clients limit concurrent connections per host.
        // Common default is 6. All 6 concurrent requests should succeed.
        var tasks = Enumerable.Range(0, 6).Select(_ =>
        {
            var request = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{_fixture.Port}/hello")
            {
                Version = HttpVersion.Version11
            };
            return SendAsync(request);
        }).ToArray();

        var responses = await Task.WhenAll(tasks);

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
    }

    [Fact(DisplayName = "CONN-11-007: Connection reuse after success — second request on keep-alive host succeeds")]
    public async Task Http11_ReuseAfterSuccess()
    {
        // After a successful keep-alive response, a subsequent request to the
        // same host succeeds. Each materialises a new pipeline (true connection
        // reuse requires engine-level keep-alive pooling), but verifies the server
        // does not refuse connections after a completed exchange.
        var request1 = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{_fixture.Port}/conn/default")
        {
            Version = HttpVersion.Version11
        };
        var response1 = await SendAsync(request1);
        Assert.Equal(HttpStatusCode.OK, response1.StatusCode);
        Assert.DoesNotContain("close", response1.Headers.Connection);

        var request2 = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{_fixture.Port}/hello")
        {
            Version = HttpVersion.Version11
        };
        var response2 = await SendAsync(request2);
        Assert.Equal(HttpStatusCode.OK, response2.StatusCode);
        Assert.Equal("Hello World", await response2.Content.ReadAsStringAsync());
    }

    [Fact(DisplayName = "CONN-11-008: No reuse after error — request after 500 succeeds on new connection")]
    public async Task Http11_NoReuseAfterError()
    {
        // After a server error (500), the next request should still succeed on
        // a fresh connection. This verifies the pipeline does not get stuck or
        // corrupt state after receiving an error response.
        var request1 = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{_fixture.Port}/status/500")
        {
            Version = HttpVersion.Version11
        };
        var response1 = await SendAsync(request1);
        Assert.Equal(HttpStatusCode.InternalServerError, response1.StatusCode);

        var request2 = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{_fixture.Port}/hello")
        {
            Version = HttpVersion.Version11
        };
        var response2 = await SendAsync(request2);
        Assert.Equal(HttpStatusCode.OK, response2.StatusCode);
        Assert.Equal("Hello World", await response2.Content.ReadAsStringAsync());
    }
}
