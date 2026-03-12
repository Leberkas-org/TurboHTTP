using System.Buffers;
using System.Net;
using Akka;
using Akka.Actor;
using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHttp.IntegrationTests.Shared;
using TurboHttp.IO;
using TurboHttp.Streams.Stages;

namespace TurboHttp.IntegrationTests.Http20;

/// <summary>
/// Integration tests for Http20Engine content encoding (decompression).
/// Verifies that the HTTP/2 pipeline transparently decompresses gzip, deflate,
/// and brotli responses (RFC 9110 §8.4) and handles large compressed bodies.
/// </summary>
public sealed class Http20ContentEncodingTests : TestKit, IClassFixture<KestrelH2Fixture>
{
    private readonly KestrelH2Fixture _fixture;
    private readonly IMaterializer _materializer;
    private readonly IActorRef _clientManager;

    public Http20ContentEncodingTests(KestrelH2Fixture fixture)
    {
        _fixture = fixture;
        _materializer = Sys.Materializer();
        _clientManager = Sys.ActorOf(Props.Create<ClientManager>());
    }

    /// <summary>
    /// Builds an HTTP/2 pipeline flow.
    /// </summary>
    private Flow<HttpRequestMessage, HttpResponseMessage, NotUsed> BuildFlow()
    {
        var requestEncoder = new Protocol.Http2RequestEncoder();
        var tcpOptions = new TcpOptions
        {
            Host = "127.0.0.1",
            Port = _fixture.Port
        };

        return Flow.FromGraph(GraphDsl.Create(b =>
        {
            var streamIdAllocator = b.Add(new StreamIdAllocatorStage());
            var requestToFrame = b.Add(new Request2FrameStage(requestEncoder));
            var frameEncoder = b.Add(new Http20EncoderStage());
            const int windowSize = 2 * 1024 * 1024;
            var prependPreface = b.Add(new PrependPrefaceStage(windowSize));
            var frameDecoder = b.Add(new Http20DecoderStage());
            var streamDecoder = b.Add(new Http20StreamStage());
            var h2Connection = b.Add(new Http20ConnectionStage(windowSize));

            var connectionStage = b.Add(new ConnectionStage(_clientManager));

            var toDataItem = b.Add(Flow.Create<(IMemoryOwner<byte>, int)>()
                .Select(ITransportItem (x) => new DataItem(x.Item1, x.Item2)));

            var connectSource = b.Add(Source.Single<ITransportItem>(new ConnectItem(tcpOptions)));
            var concat = b.Add(Concat.Create<ITransportItem>(2));

            // Request path
            b.From(streamIdAllocator.Outlet).To(requestToFrame.Inlet);
            b.From(requestToFrame.Outlet).To(h2Connection.Inlet2);

            // Outbound
            b.From(h2Connection.Outlet2).To(frameEncoder.Inlet);
            b.From(frameEncoder.Outlet).To(toDataItem.Inlet);
            b.From(connectSource).To(concat.In(0));
            b.From(toDataItem.Outlet).To(concat.In(1));
            b.From(concat.Out).To(prependPreface.Inlet);
            b.From(prependPreface.Outlet).To(connectionStage.Inlet);

            // Inbound
            b.From(connectionStage.Outlet).To(frameDecoder.Inlet);
            b.From(frameDecoder.Outlet).To(h2Connection.Inlet1);
            b.From(h2Connection.Outlet1).To(streamDecoder.Inlet);

            return new FlowShape<HttpRequestMessage, HttpResponseMessage>(
                streamIdAllocator.Inlet, streamDecoder.Outlet);
        }));
    }

    /// <summary>
    /// Sends a single HTTP/2 request through the pipeline.
    /// Each call materialises a fresh pipeline (new TCP connection + new stream IDs).
    /// </summary>
    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request)
    {
        var flow = BuildFlow();

        var (queue, responseTask) = Source.Queue<HttpRequestMessage>(1, OverflowStrategy.Backpressure)
            .Via(flow)
            .ToMaterialized(Sink.First<HttpResponseMessage>(), Keep.Both)
            .Run(_materializer);

        await queue.OfferAsync(request);
        return await responseTask.WaitAsync(TimeSpan.FromSeconds(10));
    }

    /// <summary>
    /// Generates the same deterministic payload the server produces.
    /// </summary>
    private static byte[] GenerateExpectedPayload(int kb)
    {
        var size = kb * 1024;
        var data = new byte[size];
        for (var i = 0; i < size; i++)
        {
            data[i] = (byte)('A' + (i % 26));
        }

        return data;
    }

    private HttpRequestMessage MakeGet(string path) =>
        new(HttpMethod.Get, $"http://127.0.0.1:{_fixture.Port}{path}")
        {
            Version = HttpVersion.Version20
        };

    // ── Tests ────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "20E-INT-051: GET /compress/gzip/1 decompresses gzip response over HTTP/2")]
    public async Task Gzip_Decompressed()
    {
        var response = await SendAsync(MakeGet("/compress/gzip/1"));
        var body = await response.Content.ReadAsByteArrayAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(GenerateExpectedPayload(1), body);
    }

    [Fact(DisplayName = "20E-INT-052: GET /compress/deflate/1 decompresses deflate response over HTTP/2")]
    public async Task Deflate_Decompressed()
    {
        var response = await SendAsync(MakeGet("/compress/deflate/1"));
        var body = await response.Content.ReadAsByteArrayAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(GenerateExpectedPayload(1), body);
    }

    [Fact(DisplayName = "20E-INT-053: GET /compress/br/1 decompresses brotli response over HTTP/2")]
    public async Task Brotli_Decompressed()
    {
        var response = await SendAsync(MakeGet("/compress/br/1"));
        var body = await response.Content.ReadAsByteArrayAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(GenerateExpectedPayload(1), body);
    }

    [Fact(DisplayName = "20E-INT-054: GET /compress/gzip/500 decompresses large 500KB gzip body over HTTP/2")]
    public async Task Large_Gzip_500KB_Decompressed()
    {
        var response = await SendAsync(MakeGet("/compress/gzip/500"));
        var body = await response.Content.ReadAsByteArrayAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(500 * 1024, body.Length);
        Assert.Equal(GenerateExpectedPayload(500), body);
    }
}
