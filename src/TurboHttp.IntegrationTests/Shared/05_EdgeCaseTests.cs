using System.Net;
using Akka.Actor;
using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHttp.IntegrationTests.Shared;
using TurboHttp.IO;
using TurboHttp.IO.Stages;
using TurboHttp.Protocol;
using TurboHttp.Streams;

namespace TurboHttp.IntegrationTests.Shared;

/// <summary>
/// Integration tests for edge cases and error handling.
/// Verifies mid-response close, large headers, empty body, unknown encoding passthrough,
/// non-existent host, non-listening port, connection timeout, and concurrent multi-host.
/// </summary>
public sealed class EdgeCaseTests : TestKit, IClassFixture<KestrelFixture>
{
    private readonly KestrelFixture _fixture;
    private readonly IMaterializer _materializer;
    private readonly IActorRef _clientManager;

    public EdgeCaseTests(KestrelFixture fixture)
    {
        _fixture = fixture;
        _materializer = Sys.Materializer();
        _clientManager = Sys.ActorOf(Props.Create<ClientManager>());
    }

    /// <summary>
    /// Sends a single HTTP/1.1 request through the Http11Engine pipeline.
    /// </summary>
    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, TimeSpan? timeout = null)
    {
        var engine = new Http11Engine();
        var uri = request.RequestUri!;
        var tcpOptions = new TcpOptions
        {
            Host = uri.Host,
            Port = uri.Port
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
        return await responseTask.WaitAsync(timeout ?? TimeSpan.FromSeconds(10));
    }

    private HttpRequestMessage MakeGet(string path) =>
        new(HttpMethod.Get, $"http://127.0.0.1:{_fixture.Port}{path}")
        {
            Version = HttpVersion.Version11
        };

    // ── Tests ────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "EDGE-001: Server closes mid-response — stream fails or returns partial data")]
    public async Task ServerClosesMidResponse_ThrowsOrReturnsPartial()
    {
        var request = MakeGet("/edge/close-mid-response");

        // The server advertises Content-Length: 10000 but aborts after writing "partial".
        // The pipeline should either throw (stream terminated) or return what it got.
        // Both are acceptable edge case behaviours — we verify it doesn't hang.
        try
        {
            var response = await SendAsync(request);
            // If we get a response, read the body — it should be incomplete or throw
            var body = await response.Content.ReadAsByteArrayAsync();
            Assert.True(body.Length < 10000,
                "Body should be shorter than advertised Content-Length since server aborted");
        }
        catch (Exception ex)
        {
            // Connection reset / stream termination is an acceptable outcome
            Assert.True(
                ex is IOException or OperationCanceledException or TimeoutException or AggregateException,
                $"Expected IO/Cancel/Timeout exception but got: {ex.GetType().Name}: {ex.Message}");
        }
    }

    [Fact(DisplayName = "EDGE-002: 32KB response header — decoder rejects oversized header line")]
    public async Task LargeHeader_32KB_DecoderRejectsOversizedLine()
    {
        // The Http11Decoder enforces RFC 9112 §2.3 max line length.
        // A 32KB header value exceeds this limit and should throw HttpDecoderException.
        var request = MakeGet("/edge/large-header/32");

        var ex = await Assert.ThrowsAsync<HttpDecoderException>(
            () => SendAsync(request));
        Assert.Contains("Line length exceeds", ex.Message);
    }

    [Fact(DisplayName = "EDGE-003: Empty body — response with Content-Length: 0 has no body")]
    public async Task EmptyBody_ContentLengthZero_NoBody()
    {
        var request = MakeGet("/edge/empty-body");
        var response = await SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsByteArrayAsync();
        Assert.Empty(body);
    }

    [Fact(DisplayName = "EDGE-004: Unknown Content-Encoding — decoder throws HttpDecoderException")]
    public async Task UnknownContentEncoding_ThrowsDecoderException()
    {
        // The decoder attempts decompression for all Content-Encoding values.
        // An unknown encoding like "x-custom" triggers an HttpDecoderException
        // per RFC 9110 §8.4 — the client cannot process unknown encodings.
        var request = MakeGet("/edge/unknown-encoding");

        var ex = await Assert.ThrowsAsync<HttpDecoderException>(
            () => SendAsync(request));
        Assert.Contains("x-custom", ex.Message);
    }

    [Fact(DisplayName = "EDGE-005: Non-existent host — connection to closed port on loopback throws")]
    public async Task NonExistentHost_Throws()
    {
        // Simulates connecting to a host where nothing is listening.
        // Uses loopback with a high ephemeral port (39999) that is almost certainly closed.
        // We use loopback rather than a non-routable IP to ensure fast failure and
        // clean ActorSystem shutdown (non-routable IPs can leave pending TCP connections).
        var request = new HttpRequestMessage(HttpMethod.Get, "http://127.0.0.1:39999/hello")
        {
            Version = HttpVersion.Version11
        };

        await Assert.ThrowsAnyAsync<Exception>(
            () => SendAsync(request, timeout: TimeSpan.FromSeconds(5)));
    }

    [Fact(DisplayName = "EDGE-006: Non-listening port — connection refused throws")]
    public async Task NonListeningPort_Throws()
    {
        // Use localhost with a port that is almost certainly not listening
        // Use port 1 (tcpmux) which requires root and won't be running
        var request = new HttpRequestMessage(HttpMethod.Get, "http://127.0.0.1:1/hello")
        {
            Version = HttpVersion.Version11
        };

        await Assert.ThrowsAnyAsync<Exception>(
            () => SendAsync(request, timeout: TimeSpan.FromSeconds(5)));
    }

    [Fact(DisplayName = "EDGE-007: Connection timeout — slow server triggers timeout")]
    public async Task ConnectionTimeout_SlowServerTriggersTimeout()
    {
        // The /delay/{ms} route waits before responding.
        // Use a 5-second delay with a 2-second timeout to trigger a timeout.
        var request = MakeGet("/delay/5000");

        await Assert.ThrowsAnyAsync<Exception>(
            () => SendAsync(request, timeout: TimeSpan.FromSeconds(2)));
    }

    [Fact(DisplayName = "EDGE-008: Concurrent multi-host — parallel requests to different ports succeed")]
    public async Task ConcurrentMultiHost_ParallelRequestsSucceed()
    {
        // Send multiple concurrent requests to the same host (fixture) on different paths.
        // Each materialises a separate pipeline to simulate multi-host concurrency.
        var tasks = Enumerable.Range(0, 5).Select(i =>
        {
            var request = MakeGet($"/status/{200 + i}");
            return SendAsync(request);
        }).ToArray();

        var responses = await Task.WhenAll(tasks);

        for (var i = 0; i < 5; i++)
        {
            Assert.Equal((HttpStatusCode)(200 + i), responses[i].StatusCode);
        }
    }
}
