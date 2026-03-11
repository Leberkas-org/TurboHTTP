using System.Buffers;
using System.Net;
using System.Text;
using Akka.Actor;
using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHttp.IntegrationTests.Shared;
using TurboHttp.IO;
using TurboHttp.Protocol;
using TurboHttp.Streams.Stages;

namespace TurboHttp.IntegrationTests.Http20;

/// <summary>
/// Integration tests for Http20Engine basic RFC 9113 compliance.
/// These tests drive the actual HTTP/2 Akka.Streams pipeline
/// (streamIdAllocator → request2Frame → h2Connection → encoder → prependPreface →
///  ConnectionStage/ClientManager → real TCP → decoder → h2Connection → streamDecoder)
/// against a real Kestrel h2c server.
///
/// Unlike HTTP/1.x, the Http20Engine's PrependPrefaceStage must see the ConnectItem
/// so it can inject the HTTP/2 connection preface (magic + client SETTINGS) before
/// the first request frames. We build the graph manually using Concat to feed the
/// ConnectItem through PrependPrefaceStage before the encoder's output.
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
    /// Sends a single HTTP/2 request through a manually-wired pipeline.
    /// The ConnectItem is injected via Concat before PrependPrefaceStage
    /// so the HTTP/2 connection preface is emitted before any request frames.
    /// </summary>
    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request)
    {
        var requestEncoder = new Http2RequestEncoder();
        var tcpOptions = new TcpOptions
        {
            Host = "127.0.0.1",
            Port = _fixture.Port
        };

        var flow = Flow.FromGraph(GraphDsl.Create(b =>
        {
            // ── HTTP/2 engine stages ───────────────────────────────────────────
            var streamIdAllocator = b.Add(new StreamIdAllocatorStage());
            var requestToFrame = b.Add(new Request2FrameStage(requestEncoder));
            var frameEncoder = b.Add(new Http20EncoderStage());
            const int windowSize = 2 * 1024 * 1024; // 2 MB — supports large body transfers
            var prependPreface = b.Add(new PrependPrefaceStage(windowSize));
            var frameDecoder = b.Add(new Http20DecoderStage());
            var streamDecoder = b.Add(new Http20StreamStage());
            var h2Connection = b.Add(new Http20ConnectionStage(windowSize));

            // ── Transport ──────────────────────────────────────────────────────
            var connectionStage = b.Add(new ConnectionStage(_clientManager));

            // Convert encoder output to ITransportItem (DataItem)
            var toDataItem = b.Add(Flow.Create<(IMemoryOwner<byte>, int)>()
                .Select(ITransportItem (x) => new DataItem(x.Item1, x.Item2)));

            // Concat: ConnectItem (input 0) first, then encoder DataItems (input 1)
            var connectSource = b.Add(Source.Single<ITransportItem>(new ConnectItem(tcpOptions)));
            var concat = b.Add(Concat.Create<ITransportItem>(2));

            // ── Request path ───────────────────────────────────────────────────
            // request → streamIdAllocator → request2Frame → h2Connection(outbound)
            b.From(streamIdAllocator.Outlet).To(requestToFrame.Inlet);
            b.From(requestToFrame.Outlet).To(h2Connection.Inlet2);

            // ── Outbound: h2Connection → encoder → DataItem → Concat → PrependPreface → TCP
            b.From(h2Connection.Outlet2).To(frameEncoder.Inlet);
            b.From(frameEncoder.Outlet).To(toDataItem.Inlet);
            b.From(connectSource).To(concat.In(0));
            b.From(toDataItem.Outlet).To(concat.In(1));
            b.From(concat.Out).To(prependPreface.Inlet);
            b.From(prependPreface.Outlet).To(connectionStage.Inlet);

            // ── Inbound: TCP → frameDecoder → h2Connection(inbound) → streamDecoder
            b.From(connectionStage.Outlet).To(frameDecoder.Inlet);
            b.From(frameDecoder.Outlet).To(h2Connection.Inlet1);
            b.From(h2Connection.Outlet1).To(streamDecoder.Inlet);

            return new FlowShape<HttpRequestMessage, HttpResponseMessage>(
                streamIdAllocator.Inlet, streamDecoder.Outlet);
        }));

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

        // HEAD may return 200 or 204 depending on server behaviour with HTTP/2
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
