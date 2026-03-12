using System.Net;
using System.Text;
using Akka.Actor;
using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHttp.IntegrationTests.Shared;
using TurboHttp.IO;
using TurboHttp.Streams;
using TurboHttp.Streams.Stages;

namespace TurboHttp.IntegrationTests.Http20;

/// <summary>
/// Integration tests for Http20Engine basic RFC 9113 compliance.
/// These tests drive the actual HTTP/2 Akka.Streams pipeline via Http20Engine
/// against a real Kestrel h2c server.
///
/// The transport layer prepends a ConnectItem and routes it through PrependPrefaceStage
/// so the HTTP/2 connection preface (magic + client SETTINGS) is injected before
/// the first request frames reach the ConnectionStage.
/// </summary>
public sealed class Http20BasicTests : TestKit, IClassFixture<KestrelH2Fixture>
{
    private readonly KestrelH2Fixture _fixture;
    private readonly IMaterializer _materializer;
    private readonly IActorRef _clientManager;

    public Http20BasicTests(KestrelH2Fixture fixture)
    {
        _fixture = fixture;
        _materializer = Sys.Materializer();
        _clientManager = Sys.ActorOf(Props.Create<ClientManager>());
    }

    /// <summary>
    /// Sends a single HTTP/2 request through the Http20Engine pipeline.
    /// The ConnectItem is prepended in the transport layer and routed through
    /// PrependPrefaceStage before reaching the ConnectionStage.
    /// </summary>
    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request)
    {
        var tcpOptions = new TcpOptions
        {
            Host = "127.0.0.1",
            Port = _fixture.Port
        };

        const int windowSize = 2 * 1024 * 1024;
        var engine = new Http20Engine(windowSize).CreateFlow();

        var transport =
            Flow.Create<ITransportItem>()
                .Prepend(Source.Single<ITransportItem>(new ConnectItem(tcpOptions)))
                .Via(new PrependPrefaceStage(windowSize))
                .Via(new ConnectionStage(_clientManager));

        var flow = engine.Join(transport);

        var (queue, responseTask) = Source.Queue<HttpRequestMessage>(1, OverflowStrategy.Backpressure)
            .Via(flow)
            .ToMaterialized(Sink.First<HttpResponseMessage>(), Keep.Both)
            .Run(_materializer);

        await queue.OfferAsync(request);
        return await responseTask.WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Fact(DisplayName = "20E-INT-001: GET /hello returns 200 with body via Http20Engine")]
    public async Task Get_Hello_Returns200()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{_fixture.Port}/hello")
        {
            Version = HttpVersion.Version20
        };

        var response = await SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("Hello World", body);
    }

    [Fact(DisplayName = "20E-INT-002: HEAD /hello returns status with no body via Http20Engine")]
    public async Task Head_Hello_ReturnsNoBody()
    {
        var request = new HttpRequestMessage(HttpMethod.Head, $"http://127.0.0.1:{_fixture.Port}/hello")
        {
            Version = HttpVersion.Version20
        };

        var response = await SendAsync(request);

        // HEAD may return 200 or 204 depending on server behavior with HTTP/2
        Assert.True(response.StatusCode is HttpStatusCode.OK or HttpStatusCode.NoContent);
        var body = await response.Content.ReadAsByteArrayAsync();
        Assert.Empty(body);
    }

    [Fact(DisplayName = "20E-INT-003: POST /echo returns echoed body via Http20Engine")]
    public async Task Post_Echo_ReturnsEchoedBody()
    {
        var content = new StringContent("hello-http2-engine", Encoding.UTF8, "text/plain");
        var request = new HttpRequestMessage(HttpMethod.Post, $"http://127.0.0.1:{_fixture.Port}/echo")
        {
            Version = HttpVersion.Version20,
            Content = content
        };

        var response = await SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("hello-http2-engine", body);
    }

    [Fact(DisplayName = "20E-INT-004: PUT /echo returns echoed body via Http20Engine")]
    public async Task Put_Echo_ReturnsEchoedBody()
    {
        var content = new StringContent("put-body-h2-engine", Encoding.UTF8, "text/plain");
        var request = new HttpRequestMessage(HttpMethod.Put, $"http://127.0.0.1:{_fixture.Port}/echo")
        {
            Version = HttpVersion.Version20,
            Content = content
        };

        var response = await SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("put-body-h2-engine", body);
    }

    [Theory(DisplayName = "20E-INT-005: GET /status/{code} returns correct status via Http20Engine")]
    [InlineData(200)]
    [InlineData(201)]
    [InlineData(204)]
    [InlineData(400)]
    [InlineData(404)]
    [InlineData(500)]
    public async Task Get_Status_ReturnsCorrectStatusCode(int code)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{_fixture.Port}/status/{code}")
        {
            Version = HttpVersion.Version20
        };

        var response = await SendAsync(request);

        Assert.Equal((HttpStatusCode)code, response.StatusCode);
    }

    [Fact(DisplayName = "20E-INT-006: GET /large/1024 returns 1MB body via Http20Engine")]
    public async Task Get_Large_Returns1MbBody()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{_fixture.Port}/large/1024")
        {
            Version = HttpVersion.Version20
        };

        var response = await SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(1024 * 1024, body.Length);
        Assert.All(body, b => Assert.Equal((byte)'A', b));
    }

    [Fact(DisplayName = "20E-INT-007: GET /h2/echo-path verifies pseudo-header :path via Http20Engine")]
    public async Task Get_EchoPath_VerifiesPseudoHeaders()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{_fixture.Port}/h2/echo-path")
        {
            Version = HttpVersion.Version20
        };

        var response = await SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("/h2/echo-path", body);
    }

    [Fact(DisplayName = "20E-INT-008: GET /headers/echo round-trips custom headers via Http20Engine")]
    public async Task Get_HeadersEcho_RoundTripsCustomHeaders()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{_fixture.Port}/headers/echo")
        {
            Version = HttpVersion.Version20
        };
        request.Headers.Add("X-Custom-One", "value1");
        request.Headers.Add("X-Custom-Two", "value2");

        var response = await SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("value1", response.Headers.GetValues("X-Custom-One"));
        Assert.Contains("value2", response.Headers.GetValues("X-Custom-Two"));
    }

    [Fact(DisplayName = "20E-INT-009: POST /h2/echo-binary round-trips binary body via Http20Engine")]
    public async Task Post_EchoBinary_RoundTripsBinaryBody()
    {
        var binaryData = new byte[256];
        for (var i = 0; i < binaryData.Length; i++)
        {
            binaryData[i] = (byte)i;
        }

        var content = new ByteArrayContent(binaryData);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
        var request = new HttpRequestMessage(HttpMethod.Post, $"http://127.0.0.1:{_fixture.Port}/h2/echo-binary")
        {
            Version = HttpVersion.Version20,
            Content = content
        };

        var response = await SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(binaryData, body);
    }
}
