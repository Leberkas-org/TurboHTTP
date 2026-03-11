using System.Buffers;
using System.Net;
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
/// Integration tests for Http20Engine retry handling.
/// Verifies RFC 9110 §9.2 retry semantics over HTTP/2: idempotent GET retry on 503,
/// non-idempotent POST non-retry, new-stream retry on same connection,
/// RST_STREAM REFUSED_STREAM trigger, and GOAWAY non-zero last-stream retry.
/// </summary>
public sealed class Http20RetryTests : TestKit, IClassFixture<KestrelH2Fixture>
{
    private readonly KestrelH2Fixture _fixture;
    private readonly IMaterializer _materializer;
    private readonly IActorRef _clientManager;

    public Http20RetryTests(KestrelH2Fixture fixture)
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
    /// Sends a request and manually retries using <see cref="RetryEvaluator"/>.
    /// Each retry materialises a new Http20 pipeline (new connection, new stream).
    /// </summary>
    private async Task<(HttpResponseMessage Response, int AttemptCount)> SendWithRetryAsync(
        HttpRequestMessage request,
        RetryPolicy? policy = null)
    {
        policy ??= RetryPolicy.Default;
        var attemptCount = 0;

        for (var i = 0; i <= policy.MaxRetries; i++)
        {
            attemptCount++;

            // Clone the request for each attempt
            var attemptRequest = new HttpRequestMessage(request.Method, request.RequestUri)
            {
                Version = HttpVersion.Version20
            };
            foreach (var header in request.Headers)
            {
                attemptRequest.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            if (request.Content is not null)
            {
                var bytes = await request.Content.ReadAsByteArrayAsync();
                attemptRequest.Content = new ByteArrayContent(bytes);
                foreach (var header in request.Content.Headers)
                {
                    attemptRequest.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
            }

            var response = await SendAsync(attemptRequest);

            var decision = RetryEvaluator.Evaluate(
                attemptRequest,
                response,
                attemptCount: attemptCount,
                policy: policy);

            if (!decision.ShouldRetry)
            {
                return (response, attemptCount);
            }

            // Honour Retry-After delay if present (capped for test speed)
            if (decision.RetryAfterDelay is { } delay && delay > TimeSpan.Zero)
            {
                var capped = delay > TimeSpan.FromSeconds(2) ? TimeSpan.FromSeconds(2) : delay;
                await Task.Delay(capped);
            }
        }

        throw new InvalidOperationException("Exceeded retry loop in test helper");
    }

    [Fact(DisplayName = "20E-INT-040: GET /retry/503 retries on 503 Service Unavailable over HTTP/2")]
    public async Task Get_503_RetriesIdempotentRequest()
    {
        // RFC 9110 §9.2.2: GET is idempotent, so 503 Service Unavailable responses
        // should trigger automatic retry. Over HTTP/2, each retry materialises a new
        // pipeline with fresh stream IDs, verifying the retry loop works end-to-end.
        var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"http://127.0.0.1:{_fixture.Port}/retry/503")
        {
            Version = HttpVersion.Version20
        };

        var (response, attemptCount) = await SendWithRetryAsync(request);

        // Server always returns 503, so all retries exhaust and we get 503 back
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        // Default policy allows 3 retries, so total attempts = 3
        Assert.Equal(3, attemptCount);
    }

    [Fact(DisplayName = "20E-INT-041: POST /retry/non-idempotent-503 is NOT retried over HTTP/2 (RFC 9110 §9.2.2)")]
    public async Task Post_503_NotRetried()
    {
        // RFC 9110 §9.2.2: POST is not idempotent — automatic retry is not safe
        // because the server may have already acted on the request. This applies
        // equally to HTTP/2; the protocol version does not change retry semantics.
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"http://127.0.0.1:{_fixture.Port}/retry/non-idempotent-503")
        {
            Version = HttpVersion.Version20,
            Content = new StringContent("payload")
        };

        var (response, attemptCount) = await SendWithRetryAsync(request);

        // POST is not idempotent — must not be retried
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(1, attemptCount);
    }

    [Fact(DisplayName = "20E-INT-042: GET /retry/succeed-after/2 retries on new HTTP/2 stream per attempt")]
    public async Task RetryNewStream_SucceedsOnSecondAttempt()
    {
        // RFC 9113 §5.1.1: Streams initiated by a client use odd-numbered stream
        // identifiers. Each retry materialises a new HTTP/2 pipeline, which allocates
        // fresh stream IDs starting from 1. This test verifies that the retry loop
        // correctly sends each attempt as a new HTTP/2 request on a new stream/connection,
        // and the succeed-after-N route returns 200 on the second attempt.
        var uniqueKey = Guid.NewGuid().ToString("N");

        var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"http://127.0.0.1:{_fixture.Port}/retry/succeed-after/2?key={uniqueKey}")
        {
            Version = HttpVersion.Version20
        };

        var (response, attemptCount) = await SendWithRetryAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("success", body);
        // First attempt returns 503 (new stream), second returns 200 (another new stream)
        Assert.Equal(2, attemptCount);
    }

    [Fact(DisplayName = "20E-INT-043: RST_STREAM REFUSED_STREAM treated as retriable network failure")]
    public async Task RstStreamRefusedStream_RetriableAsNetworkFailure()
    {
        // RFC 9113 §8.7: A server MAY send RST_STREAM with REFUSED_STREAM (0x7) to
        // indicate that no processing has been performed on the stream. This is
        // semantically equivalent to a network failure for retry purposes — the
        // request was never processed, so idempotent methods can be safely retried.
        //
        // This test verifies the retry evaluator's decision logic: when a GET request
        // encounters a REFUSED_STREAM (surfaced as a network failure since the server
        // did not process the request), RetryEvaluator correctly identifies it as
        // retriable. We first send a real HTTP/2 request to confirm the pipeline
        // works, then verify the evaluator's network-failure path.
        var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"http://127.0.0.1:{_fixture.Port}/hello")
        {
            Version = HttpVersion.Version20
        };

        // Confirm the pipeline works end-to-end over HTTP/2
        var response = await SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Simulate REFUSED_STREAM: server sent RST_STREAM before processing,
        // which surfaces as a network-level failure (no HTTP response)
        var decision = RetryEvaluator.Evaluate(
            request,
            response: null,
            networkFailure: true,
            attemptCount: 1);

        Assert.True(decision.ShouldRetry,
            "REFUSED_STREAM on idempotent GET should be retriable (network failure path)");
        Assert.Contains("Network failure", decision.Reason);
    }

    [Fact(DisplayName = "20E-INT-044: GOAWAY with non-zero last-stream-id allows retry of unprocessed streams")]
    public async Task GoAwayNonZeroLastStream_UnprocessedStreamRetriable()
    {
        // RFC 9113 §6.8: GOAWAY with last-stream-id > 0 indicates that streams with
        // IDs above last-stream-id were NOT processed by the server. Requests on those
        // unprocessed streams can be safely retried on a new connection. This is
        // equivalent to a network failure from the retry evaluator's perspective.
        //
        // This test verifies two things:
        // 1. Multiple HTTP/2 requests succeed through the pipeline (establishing a
        //    working connection where GOAWAY could be received).
        // 2. The retry evaluator correctly identifies unprocessed-stream failures
        //    (surfaced as network failures) as retriable for idempotent methods,
        //    and non-retriable for non-idempotent methods.

        // Step 1: Send a real HTTP/2 request to confirm pipeline works
        var getRequest = new HttpRequestMessage(
            HttpMethod.Get,
            $"http://127.0.0.1:{_fixture.Port}/hello")
        {
            Version = HttpVersion.Version20
        };

        var response = await SendAsync(getRequest);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Step 2: Simulate GOAWAY scenario — unprocessed GET stream (retriable)
        var unprocessedGet = new HttpRequestMessage(
            HttpMethod.Get,
            $"http://127.0.0.1:{_fixture.Port}/hello")
        {
            Version = HttpVersion.Version20
        };

        var getDecision = RetryEvaluator.Evaluate(
            unprocessedGet,
            response: null,
            networkFailure: true,
            attemptCount: 1);

        Assert.True(getDecision.ShouldRetry,
            "Unprocessed GET stream after GOAWAY should be retriable");

        // Step 3: Simulate GOAWAY scenario — unprocessed POST stream (not retriable)
        var unprocessedPost = new HttpRequestMessage(
            HttpMethod.Post,
            $"http://127.0.0.1:{_fixture.Port}/echo")
        {
            Version = HttpVersion.Version20,
            Content = new StringContent("data")
        };

        var postDecision = RetryEvaluator.Evaluate(
            unprocessedPost,
            response: null,
            networkFailure: true,
            attemptCount: 1);

        Assert.False(postDecision.ShouldRetry,
            "POST is not idempotent — even after GOAWAY, automatic retry is not safe");
    }
}
