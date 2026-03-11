using System.Net;
using Akka.Actor;
using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHttp.IntegrationTests.Shared;
using TurboHttp.IO;
using TurboHttp.Streams;
using TurboHttp.Streams.Stages;

namespace TurboHttp.IntegrationTests.Http10;

/// <summary>
/// Integration tests for Http10Engine connection management.
/// Verifies HTTP/1.0 no-keep-alive default and opt-in keep-alive
/// behaviour against real Kestrel connection reuse routes.
/// </summary>
public sealed class Http10ConnectionTests : TestKit, IClassFixture<KestrelFixture>
{
    private readonly KestrelFixture _fixture;
    private readonly IMaterializer _materializer;
    private readonly IActorRef _clientManager;

    public Http10ConnectionTests(KestrelFixture fixture)
    {
        _fixture = fixture;
        _materializer = Sys.Materializer();
        _clientManager = Sys.ActorOf(Props.Create<ClientManager>());
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request)
    {
        var engine = new Http10Engine();
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

    [Fact(DisplayName = "CONN-10-001: HTTP/1.0 default has no keep-alive — connection closes after response")]
    public async Task Http10_Default_NoKeepAlive()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{_fixture.Port}/conn/default")
        {
            Version = HttpVersion.Version10
        };

        var response = await SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("default", body);
        // HTTP/1.0 default: no Connection: Keep-Alive in response means connection closes
        Assert.False(
            response.Headers.Connection.Contains("Keep-Alive"),
            "HTTP/1.0 default response should not contain Connection: Keep-Alive");
    }

    [Fact(DisplayName = "CONN-10-002: HTTP/1.0 opt-in keep-alive returns Connection: Keep-Alive")]
    public async Task Http10_OptIn_KeepAlive()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{_fixture.Port}/conn/keep-alive")
        {
            Version = HttpVersion.Version10
        };
        request.Headers.Connection.Add("Keep-Alive");

        var response = await SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("keep-alive", body);
        Assert.Contains("Keep-Alive", response.Headers.Connection);
    }

    [Fact(DisplayName = "CONN-10-003: Sequential HTTP/1.0 requests each succeed on new connection")]
    public async Task Http10_Sequential_NewConnection()
    {
        var request1 = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{_fixture.Port}/conn/default")
        {
            Version = HttpVersion.Version10
        };
        var request2 = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{_fixture.Port}/conn/default")
        {
            Version = HttpVersion.Version10
        };

        var response1 = await SendAsync(request1);
        var response2 = await SendAsync(request2);

        Assert.Equal(HttpStatusCode.OK, response1.StatusCode);
        Assert.Equal(HttpStatusCode.OK, response2.StatusCode);
        var body1 = await response1.Content.ReadAsStringAsync();
        var body2 = await response2.Content.ReadAsStringAsync();
        Assert.Equal("default", body1);
        Assert.Equal("default", body2);
    }

    [Fact(DisplayName = "CONN-10-004: HTTP/1.0 Keep-Alive sequential requests both receive Keep-Alive response")]
    public async Task Http10_KeepAlive_ConnectionReuse()
    {
        // Send two sequential requests with Connection: Keep-Alive.
        // Both succeed and both responses confirm Keep-Alive, proving the
        // server honours the opt-in. Each uses a separate pipeline materialisation
        // (true in-pipeline connection reuse requires engine-level keep-alive wiring).
        var request1 = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{_fixture.Port}/conn/keep-alive")
        {
            Version = HttpVersion.Version10
        };
        request1.Headers.Connection.Add("Keep-Alive");

        var request2 = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{_fixture.Port}/conn/keep-alive")
        {
            Version = HttpVersion.Version10
        };
        request2.Headers.Connection.Add("Keep-Alive");

        var response1 = await SendAsync(request1);
        var response2 = await SendAsync(request2);

        Assert.Equal(HttpStatusCode.OK, response1.StatusCode);
        Assert.Equal(HttpStatusCode.OK, response2.StatusCode);

        var body1 = await response1.Content.ReadAsStringAsync();
        var body2 = await response2.Content.ReadAsStringAsync();
        Assert.Equal("keep-alive", body1);
        Assert.Equal("keep-alive", body2);

        Assert.Contains("Keep-Alive", response1.Headers.Connection);
        Assert.Contains("Keep-Alive", response2.Headers.Connection);
    }

    [Fact(DisplayName = "CONN-10-005: Server Connection: close overrides client Keep-Alive")]
    public async Task Http10_ServerClose_OverridesKeepAlive()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{_fixture.Port}/conn/close")
        {
            Version = HttpVersion.Version10
        };
        request.Headers.Connection.Add("Keep-Alive");

        var response = await SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("closing", body);
        // Server explicitly sends Connection: close, overriding client's Keep-Alive
        Assert.Contains("close", response.Headers.Connection);
    }
}
