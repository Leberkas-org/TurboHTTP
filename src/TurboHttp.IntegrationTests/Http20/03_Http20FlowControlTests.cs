using System.Buffers;
using System.Collections.Concurrent;
using System.Net;
using System.Text;
using Akka;
using Akka.Actor;
using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHttp.IntegrationTests.Shared;
using TurboHttp.IO;
using TurboHttp.Protocol;
using TurboHttp.Streams.Stages;

namespace TurboHttp.IntegrationTests.Http20;

/// <summary>
/// Integration tests for Http20Engine flow control (RFC 9113 §6.9).
/// Verifies that large POST bodies respect flow control windows, WINDOW_UPDATE
/// frames increase capacity, connection/stream-level windows are independent,
/// and responses arrive correctly even with small initial windows.
/// </summary>
public sealed class Http20FlowControlTests : TestKit, IClassFixture<KestrelH2Fixture>
{
    private readonly KestrelH2Fixture _fixture;
    private readonly IMaterializer _materializer;
    private readonly IActorRef _clientManager;

    public Http20FlowControlTests(KestrelH2Fixture fixture)
    {
        _fixture = fixture;
        _materializer = Sys.Materializer();
        _clientManager = Sys.ActorOf(Props.Create<ClientManager>());
    }

    /// <summary>
    /// Builds an HTTP/2 pipeline flow with configurable initial window size.
    /// </summary>
    private Flow<HttpRequestMessage, HttpResponseMessage, NotUsed> BuildFlow(int windowSize)
    {
        var requestEncoder = new Http2RequestEncoder();
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
    /// Sends a single HTTP/2 request through a pipeline with configurable window size.
    /// </summary>
    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, int windowSize = 2 * 1024 * 1024)
    {
        var flow = BuildFlow(windowSize);

        var (queue, responseTask) = Source.Queue<HttpRequestMessage>(1, OverflowStrategy.Backpressure)
            .Via(flow)
            .ToMaterialized(Sink.First<HttpResponseMessage>(), Keep.Both)
            .Run(_materializer);

        await queue.OfferAsync(request);
        return await responseTask.WaitAsync(TimeSpan.FromSeconds(15));
    }

    /// <summary>
    /// Sends multiple HTTP/2 requests through a single pipeline with configurable window size.
    /// </summary>
    private async Task<List<HttpResponseMessage>> SendManyAsync(
        List<HttpRequestMessage> requests,
        int windowSize = 2 * 1024 * 1024,
        TimeSpan? timeout = null)
    {
        var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(15);
        var flow = BuildFlow(windowSize);

        var responses = new ConcurrentBag<HttpResponseMessage>();
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var (queue, _) = Source.Queue<HttpRequestMessage>(requests.Count + 1, OverflowStrategy.Backpressure)
            .Via(flow)
            .ToMaterialized(
                Sink.ForEach<HttpResponseMessage>(res =>
                {
                    responses.Add(res);
                    if (responses.Count >= requests.Count)
                    {
                        tcs.TrySetResult();
                    }
                }),
                Keep.Both)
            .Run(_materializer);

        foreach (var request in requests)
        {
            await queue.OfferAsync(request);
        }

        await tcs.Task.WaitAsync(effectiveTimeout);
        return responses.ToList();
    }

    [Fact(DisplayName = "20E-INT-016: Large POST body within flow control window is delivered correctly")]
    public async Task LargePost_WithinFlowControlWindow_DeliveredCorrectly()
    {
        // RFC 9113 §6.9: A sender MUST NOT send a flow-controlled frame with a length
        // that exceeds the available space in either the connection or stream flow-control window.
        // The server's default initial window is 65535 bytes. A 32KB body fits within this window,
        // verifying that the pipeline correctly sends large POST bodies through flow control.
        const int bodySize = 32 * 1024;
        var bodyBytes = new byte[bodySize];
        Array.Fill(bodyBytes, (byte)'B');

        var content = new ByteArrayContent(bodyBytes);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
        var request = new HttpRequestMessage(HttpMethod.Post, $"http://127.0.0.1:{_fixture.Port}/echo")
        {
            Version = HttpVersion.Version20,
            Content = content
        };

        var response = await SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var responseBody = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(bodySize, responseBody.Length);
        Assert.All(responseBody, b => Assert.Equal((byte)'B', b));
    }

    [Fact(DisplayName = "20E-INT-017: WINDOW_UPDATE enables large response beyond default 65535 window")]
    public async Task WindowUpdate_EnablesLargeResponseBeyondDefaultWindow()
    {
        // RFC 9113 §6.9.1: The client advertises a 2MB INITIAL_WINDOW_SIZE via SETTINGS
        // and sends WINDOW_UPDATE frames to replenish capacity. The 512KB response
        // far exceeds the default 65535-byte initial window, proving that the client's
        // WINDOW_UPDATE mechanism enables the server to deliver the full response.
        var request = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{_fixture.Port}/large/512")
        {
            Version = HttpVersion.Version20
        };

        var response = await SendAsync(request, windowSize: 2 * 1024 * 1024);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(512 * 1024, body.Length);
        Assert.All(body, b => Assert.Equal((byte)'A', b));
    }

    [Fact(DisplayName = "20E-INT-018: Connection and stream flow control levels are independent")]
    public async Task ConnectionAndStream_FlowControlLevelsIndependent()
    {
        // RFC 9113 §6.9: Flow control operates at two levels — connection and individual stream.
        // Send concurrent requests with different body sizes to verify that one stream's
        // flow control consumption does not block another stream's progress.
        var requests = new List<HttpRequestMessage>
        {
            // Large response (64KB of 'A')
            new(HttpMethod.Get, $"http://127.0.0.1:{_fixture.Port}/large/64")
            {
                Version = HttpVersion.Version20
            },
            // Small response ("pong")
            new(HttpMethod.Get, $"http://127.0.0.1:{_fixture.Port}/ping")
            {
                Version = HttpVersion.Version20
            },
            // Medium response (8KB of 'P')
            new(HttpMethod.Get, $"http://127.0.0.1:{_fixture.Port}/h2/priority/8")
            {
                Version = HttpVersion.Version20
            }
        };

        var responses = await SendManyAsync(requests);

        Assert.Equal(3, responses.Count);
        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));

        var bodies = new List<byte[]>();
        foreach (var r in responses)
        {
            bodies.Add(await r.Content.ReadAsByteArrayAsync());
        }

        // Verify each response arrived with correct content
        Assert.Contains(bodies, b => b.Length == 64 * 1024 && b.All(x => x == (byte)'A'));
        Assert.Contains(bodies, b => Encoding.UTF8.GetString(b) == "pong");
        Assert.Contains(bodies, b => b.Length == 8 * 1024 && b.All(x => x == (byte)'P'));
    }

    [Fact(DisplayName = "20E-INT-019: Response succeeds with RFC-default 65535 initial window")]
    public async Task DefaultWindow_ResponseStillSucceeds()
    {
        // RFC 9113 §6.9.2: The initial value for the flow-control window is 65535 bytes.
        // Verify the pipeline operates correctly using the RFC-default window size (65535),
        // receiving a 32KB response that fits within the initial window allocation.
        var request = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{_fixture.Port}/h2/priority/32")
        {
            Version = HttpVersion.Version20
        };

        // Use the RFC-default window size — the smallest practical window
        var response = await SendAsync(request, windowSize: 65535);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(32 * 1024, body.Length);
        Assert.All(body, b => Assert.Equal((byte)'P', b));
    }
}
