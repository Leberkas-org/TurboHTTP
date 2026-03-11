using System.Net;
using System.Text;
using Akka.Actor;
using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHttp.IntegrationTests.Shared;
using TurboHttp.IO;
using TurboHttp.Streams;
using TurboHttp.Streams.Stages;

namespace TurboHttp.IntegrationTests.Http10;

/// <summary>
/// Integration tests for Http10Engine basic RFC 1945 compliance.
/// these tests drive the actual Http10Engine Akka.Streams pipeline
/// (encoder → ConnectionStage/ClientManager → real TCP → decoder → correlator)
/// against a real Kestrel server.
/// </summary>
public sealed class Http10EngineBasicTests : TestKit, IClassFixture<KestrelFixture>
{
    private readonly KestrelFixture _fixture;
    private readonly IMaterializer _materializer;
    private readonly IActorRef _clientManager;

    public Http10EngineBasicTests(KestrelFixture fixture)
    {
        _fixture = fixture;
        _materializer = Sys.Materializer();
        _clientManager = Sys.ActorOf(Props.Create<ClientManager>());
    }

    /// <summary>
    /// Sends a single HTTP/1.0 request through the Http10Engine pipeline
    /// using the real ConnectionStage + ClientManager TCP transport.
    /// A ConnectItem is prepended via Concat so the ConnectionStage knows
    /// where to connect. Source.Queue prevents premature upstream completion
    /// before the TCP response arrives.
    /// </summary>
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
        
        // Use Source.Queue to prevent premature stream completion.
        // Sink.First captures the response and cancels the stream.
        var (queue, responseTask) = Source.Queue<HttpRequestMessage>(1, OverflowStrategy.Backpressure)
            .Via(flow)
            .ToMaterialized(Sink.First<HttpResponseMessage>(), Keep.Both)
            .Run(_materializer);

        await queue.OfferAsync(request);
        return await responseTask.WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Fact(DisplayName = "ENG-INT-001: GET /hello returns 200 with body via Http10Engine")]
    public async Task Get_Hello_Returns200WithBody()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{_fixture.Port}/hello")
        {
            Version = HttpVersion.Version10
        };

        var response = await SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("Hello World", body);
    }

    [Fact(DisplayName = "ENG-INT-002: HEAD /hello returns 204 with no body via Http10Engine")]
    public async Task Head_Hello_Returns200WithNoBody()
    {
        var request = new HttpRequestMessage(HttpMethod.Head, $"http://127.0.0.1:{_fixture.Port}/hello")
        {
            Version = HttpVersion.Version10
        };

        var response = await SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Empty(body);
    }

    [Fact(DisplayName = "ENG-INT-003: POST /echo returns echoed body via Http10Engine")]
    public async Task Post_Echo_ReturnsEchoedBody()
    {
        var content = new StringContent("hello-http10-engine", Encoding.UTF8, "text/plain");
        var request = new HttpRequestMessage(HttpMethod.Post, $"http://127.0.0.1:{_fixture.Port}/echo")
        {
            Version = HttpVersion.Version10,
            Content = content
        };

        var response = await SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("hello-http10-engine", body);
    }

    [Fact(DisplayName = "ENG-INT-004: PUT /echo returns echoed body via Http10Engine")]
    public async Task Put_Echo_ReturnsEchoedBody()
    {
        var content = new StringContent("put-body-engine", Encoding.UTF8, "text/plain");
        var request = new HttpRequestMessage(HttpMethod.Put, $"http://127.0.0.1:{_fixture.Port}/echo")
        {
            Version = HttpVersion.Version10,
            Content = content
        };

        var response = await SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("put-body-engine", body);
    }

    [Fact(DisplayName = "ENG-INT-005: DELETE /any returns method name via Http10Engine")]
    public async Task Delete_Any_ReturnsMethodName()
    {
        var request = new HttpRequestMessage(HttpMethod.Delete, $"http://127.0.0.1:{_fixture.Port}/any")
        {
            Version = HttpVersion.Version10
        };

        var response = await SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("DELETE", body);
    }

    [Theory(DisplayName = "ENG-INT-006: GET /status/{code} returns correct status via Http10Engine")]
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
            Version = HttpVersion.Version10
        };

        var response = await SendAsync(request);

        Assert.Equal((HttpStatusCode)code, response.StatusCode);
    }

    [Fact(DisplayName = "ENG-INT-007: GET /large/100 returns 100KB body via Http10Engine")]
    public async Task Get_Large_Returns100KbBody()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{_fixture.Port}/large/100")
        {
            Version = HttpVersion.Version10
        };

        var response = await SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(100 * 1024, body.Length);
        Assert.All(body, b => Assert.Equal((byte)'A', b));
    }

    [Fact(DisplayName = "ENG-INT-008: GET /headers/echo round-trips custom headers via Http10Engine")]
    public async Task Get_HeadersEcho_RoundTripsCustomHeaders()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{_fixture.Port}/headers/echo")
        {
            Version = HttpVersion.Version10
        };
        request.Headers.Add("X-Custom-One", "value1");
        request.Headers.Add("X-Custom-Two", "value2");

        var response = await SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("value1", response.Headers.GetValues("X-Custom-One"));
        Assert.Contains("value2", response.Headers.GetValues("X-Custom-Two"));
    }

    [Fact(DisplayName = "ENG-INT-009: GET /multiheader returns multi-value headers via Http10Engine")]
    public async Task Get_MultiHeader_ReturnsMultipleValues()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{_fixture.Port}/multiheader")
        {
            Version = HttpVersion.Version10
        };

        var response = await SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var values = response.Headers.GetValues("X-Value").ToList();
        Assert.Contains("alpha", values);
        Assert.Contains("beta", values);
    }

    [Fact(DisplayName = "ENG-INT-010: GET /empty-cl returns 200 with empty body via Http10Engine")]
    public async Task Get_EmptyCl_ReturnsEmptyBody()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{_fixture.Port}/empty-cl")
        {
            Version = HttpVersion.Version10
        };

        var response = await SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsByteArrayAsync();
        Assert.Empty(body);
    }
}