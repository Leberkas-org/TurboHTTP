using System.Net;
using System.Text;
using System.Threading.Channels;
using Akka;
using Akka.Actor;
using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHTTP.Internal;
using TurboHTTP.Protocol.Http2;
using TurboHTTP.Streams;
using TurboHTTP.Streams.Stages.Decoding;
using TurboHTTP.Streams.Stages.Encoding;
using TurboHTTP.Transport.Connection;
using TurboHTTP.Transport.Tcp;

namespace TurboHTTP.StreamTests.Streams;

/// <summary>
/// Integration tests proving that the Akka.Streams pipeline ("the stream") survives
/// various error conditions end-to-end.
/// </summary>
/// <remarks>
/// Validates the "Stream Never Dies" principle from plan_003:
/// only <c>Dispose()</c> (via <c>queue.Complete()</c>) terminates the stream.
/// Individual errors — encoding failures, corrupt responses, connection drops,
/// GOAWAY, timeouts, HTTP 5xx — are absorbed without killing the pipeline.
/// </remarks>
public sealed class StreamSurvivalSpec : EngineTestBase
{
    // Helpers

    private static byte[] Http11Ok() =>
        "HTTP/1.1 200 OK\r\nContent-Length: 0\r\n\r\n"u8.ToArray();

    private static byte[] Http11ServerError() =>
        "HTTP/1.1 500 Internal Server Error\r\nContent-Length: 0\r\n\r\n"u8.ToArray();

    private static HttpRequestMessage ValidHttp11Request(string path = "/") =>
        new(HttpMethod.Get, $"http://example.com{path}")
        {
            Version = HttpVersion.Version11
        };

    private static IInputItem MakeChunk(string ascii)
    {
        var bytes = Encoding.Latin1.GetBytes(ascii);
        return NetworkBuffer.FromArray(bytes);
    }

    /// <summary>
    /// Builds a persistent pipeline backed by a <see cref="Source.Queue{T}"/> as the request source.
    /// Returns the queue, a channel that receives every response, and the <see cref="Sink.ForEach{T}"/>
    /// materialized task (which completes or faults together with the stream).
    /// </summary>
    private (ISourceQueueWithComplete<HttpRequestMessage> Queue,
        Channel<HttpResponseMessage> Responses,
        Task PipelineTask) BuildPersistentPipeline(Func<byte[]> responseFactory)
    {
        var responses = Channel.CreateUnbounded<HttpResponseMessage>();
        var engine = new Engine();
        var transports = new TransportRegistry()
            .Register(new Version(1, 0), new DelegateTransportFactory(() => Flow.FromGraph(new EngineFakeConnectionStage(responseFactory))))
            .Register(new Version(1, 1), new DelegateTransportFactory(() => Flow.FromGraph(new EngineFakeConnectionStage(responseFactory))))
            .Register(new Version(2, 0), new DelegateTransportFactory(() => Flow.FromGraph(new EngineFakeConnectionStage(responseFactory))))
            .Register(new Version(3, 0), new DelegateTransportFactory(() => Flow.FromGraph(new EngineFakeConnectionStage(responseFactory))));
        var flow = engine.CreateFlow(transports, PipelineDescriptor.Empty);

        var (queue, pipelineTask) = Source.Queue<HttpRequestMessage>(256, OverflowStrategy.Backpressure)
            .Via(flow)
            .ToMaterialized(
                Sink.ForEach<HttpResponseMessage>(r => responses.Writer.TryWrite(r)),
                Keep.Both)
            .Run(Materializer);

        return (queue, responses, pipelineTask);
    }

    [Fact(Timeout = 10_000)]
    public async Task StreamSurvival_should_encode_subsequent_request_when_null_uri_request_causes_encoder_error()
    {
        // A request with a null URI causes RequestEndpoint.FromRequest to throw
        // inside the encoder's onPush handler.
        // The stage must log the error, drop the bad element, and pull the next one
        // without calling FailStage — the stream must stay alive.
        var nullUriRequest = new HttpRequestMessage(HttpMethod.Get, (Uri?)null)
        {
            Version = HttpVersion.Version11
        };
        var validRequest = ValidHttp11Request("/after-error");

        var results = await Source.From([nullUriRequest, validRequest])
            .Via(Flow.FromGraph(new Http11EncoderStage()))
            .RunWith(Sink.Seq<IOutputItem>(), Materializer);

        // Bad request dropped by the encoder; only the valid request produced a NetworkBuffer.
        Assert.Single(results);
        var dataItem = Assert.IsAssignableFrom<NetworkBuffer>(results[0]);
        Assert.True(dataItem.Length > 0, "Encoded NetworkBuffer must contain bytes");
        dataItem.Dispose();
    }

    [Fact(Timeout = 10_000)]
    public async Task StreamSurvival_should_decode_subsequent_response_when_corrupt_bytes_arrive_first()
    {
        // Corrupt TCP bytes cause HttpDecoderException inside the decoder.
        // The stage must log, reset, and continue pulling — not FailStage.
        // The next valid HTTP/1.1 response must be decoded and emitted.
        var garbage = MakeChunk("GARBAGE CORRUPT BYTES\r\n\r\n");
        var valid = MakeChunk("HTTP/1.1 200 OK\r\nContent-Length: 0\r\n\r\n");

        var response = await Source.From([garbage, valid])
            .Via(Flow.FromGraph(new Http11DecoderStage()))
            .RunWith(Sink.First<HttpResponseMessage>(), Materializer);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact(Timeout = 10_000)]
    public async Task StreamSurvival_should_drop_network_buffer_without_faulting_when_connection_handle_is_null()
    {
        // Send a NetworkBuffer directly to ConnectionStage without a prior ConnectItem.
        // In initial state _handle is null, so the stage must log a warning and drop
        // the element via TryPull() without calling FailStage.
        var dataItem = NetworkBuffer.FromArray([0xAB, 0xAB, 0xAB, 0xAB]);

        // Actor is never consulted — no ConnectItem is sent, so AcquireAsync is never called.
        var actor = Sys.ActorOf(Props.Create(() => new ConnectionManagerActor(TimeSpan.FromMinutes(5))));
        var stage = new TcpConnectionStage(actor, new TurboClientOptions());

        var results = await Source.From(new IOutputItem[] { dataItem })
            .Via(Flow.FromGraph(stage))
            .RunWith(Sink.Seq<IInputItem>(), Materializer);

        // NetworkBuffer was silently dropped; stage completed cleanly with no output.
        Assert.Empty(results);
    }

    [Fact(Timeout = 10_000)]
    public async Task
        StreamSurvival_should_not_fault_pipeline_tasks_when_go_away_received_and_subsequent_request_dropped()
    {
        // The server sends GOAWAY (lastStreamId = 1) to signal graceful shutdown.
        // After GOAWAY, any new request (stream ID 3) must be silently dropped.
        // Neither the downstream nor the server-bound pipeline task may fault.
        var goAway = new GoAwayFrame(lastStreamId: 1, Http2ErrorCode.NoError);
        var lateRequest = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");

        var downstreamSink = Sink.Seq<HttpResponseMessage>();
        var serverBoundSink = Sink.Seq<Http2Frame>();

        var graph = RunnableGraph.FromGraph(
            GraphDsl.Create(downstreamSink, serverBoundSink,
                (m1, m2) => (m1, m2),
                (b, dsSink, sbSink) =>
                {
                    var connStage = b.Add(new Http20ConnectionStage());
                    var signalSink = b.Add(
                        Sink.Ignore<IControlItem>().MapMaterializedValue(_ => NotUsed.Instance));

                    // Server sends GOAWAY then stays open indefinitely
                    var serverSource = b.Add(
                        Source.Single<Http2Frame>(goAway)
                            .Concat(Source.Never<Http2Frame>()));

                    // App sends a new request 150 ms after GOAWAY (after it has been processed)
                    var requestSource = b.Add(
                        Source.Single(lateRequest)
                            .InitialDelay(TimeSpan.FromMilliseconds(150))
                            .Concat(Source.Never<HttpRequestMessage>()));

                    b.From(serverSource).To(connStage.InServer);
                    b.From(connStage.OutResponse).To(dsSink);
                    b.From(requestSource).To(connStage.InApp);
                    b.From(connStage.OutServer).To(sbSink);
                    b.From(connStage.OutSignal).To(signalSink);

                    return ClosedShape.Instance;
                }));

        var (downstreamTask, serverBoundTask) = graph.Run(Materializer);

        // Allow both GOAWAY and the delayed request to be processed
        await Task.Delay(TimeSpan.FromMilliseconds(500), TestContext.Current.CancellationToken);

        Assert.False(downstreamTask.IsFaulted,
            "Downstream task must not fault after GOAWAY + dropped request");
        Assert.False(serverBoundTask.IsFaulted,
            "ServerBound task must not fault after GOAWAY + dropped request");
    }

    // SURV-005: Pipeline not faulted when server never responds (timeout scenario)

    [Fact(Timeout = 10_000)]
    public async Task StreamSurvival_should_not_fault_pipeline_when_sent_request_receives_no_response()
    {
        // A "blackhole" connection absorbs all outbound bytes and never produces inbound data.
        // The decoder stage blocks awaiting data; the pipeline must stay alive (not fault).
        var blackhole = Flow.FromSinkAndSource<IOutputItem, IInputItem, NotUsed>(
            Sink.Ignore<IOutputItem>().MapMaterializedValue(_ => NotUsed.Instance),
            Source.Never<IInputItem>());

        var flow = new Http11Engine().CreateFlow().Join(blackhole);

        var (queue, pipelineTask) = Source.Queue<HttpRequestMessage>(16, OverflowStrategy.Backpressure)
            .Via(flow)
            .ToMaterialized(
                Sink.Ignore<HttpResponseMessage>(),
                Keep.Both)
            .Run(Materializer);

        // Send a request — encoded bytes go to the blackhole, no response ever arrives
        await queue.OfferAsync(ValidHttp11Request("/timeout-test"));

        // Give any potential errors time to manifest
        await Task.Delay(TimeSpan.FromMilliseconds(300), TestContext.Current.CancellationToken);

        // Stream must be alive: not faulted, not completed
        Assert.False(pipelineTask.IsFaulted, "Pipeline must not fault when no response arrives");
        Assert.False(pipelineTask.IsCompletedSuccessfully,
            "Pipeline must still be running — not yet completed");

        queue.Complete();
    }

    // SURV-006: 100 sequential requests, every 10th returns HTTP 500

    [Fact(Timeout = 30_000)]
    public async Task StreamSurvival_should_complete_100_requests_when_every_10th_returns_server_error()
    {
        // The response factory returns HTTP 500 for every 10th call, HTTP 200 otherwise.
        // All 100 requests are processed; the stream must never fault.
        const int total = 100;
        var requestCount = 0;

        var factory = () =>
        {
            requestCount++;
            return requestCount % 10 == 0
                ? Http11ServerError()
                : Http11Ok();
        };

        var (queue, responses, pipelineTask) = BuildPersistentPipeline(factory);

        var successCount = 0;
        var errorCount = 0;

        for (var i = 1; i <= total; i++)
        {
            await queue.OfferAsync(new HttpRequestMessage(HttpMethod.Get, $"http://example.com/item/{i}")
            {
                Version = HttpVersion.Version11
            });

            var response = await responses.Reader.ReadAsync(TestContext.Current.CancellationToken)
                .AsTask()
                .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

            if (response.StatusCode == HttpStatusCode.OK)
            {
                successCount++;
            }
            else
            {
                errorCount++;
            }
        }

        Assert.Equal(90, successCount);
        Assert.Equal(10, errorCount);
        Assert.False(pipelineTask.IsFaulted,
            "Pipeline must not fault after processing 10 server-error responses");

        queue.Complete();
    }

    [Fact(Timeout = 10_000)]
    public async Task StreamSurvival_should_keep_stream_alive_when_requests_processed_but_queue_not_completed()
    {
        // The pipeline must remain running after each response is received.
        // It must only signal completion once the caller disposes (queue.Complete()).
        var (queue, responses, pipelineTask) = BuildPersistentPipeline(Http11Ok);

        for (var i = 0; i < 3; i++)
        {
            await queue.OfferAsync(ValidHttp11Request($"/item-{i}"));
            await responses.Reader.ReadAsync(TestContext.Current.CancellationToken)
                .AsTask()
                .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        }

        // After 3 successful round-trips the pipeline must still be running
        Assert.False(pipelineTask.IsCompleted,
            "Pipeline must not complete while the source queue is still open");
        Assert.False(pipelineTask.IsFaulted,
            "Pipeline must not fault during normal request processing");

        queue.Complete();
    }

    [Fact(Timeout = 10_000)]
    public async Task StreamSurvival_should_complete_all_stages_cleanly_when_source_queue_completed()
    {
        // Process one request to confirm the pipeline is operational.
        // Then complete the source queue (simulating Dispose) and verify
        // no exception is thrown during shutdown.
        var (queue, responses, pipelineTask) = BuildPersistentPipeline(Http11Ok);

        await queue.OfferAsync(ValidHttp11Request("/shutdown-test"));
        await responses.Reader.ReadAsync(TestContext.Current.CancellationToken)
            .AsTask()
            .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        // Simulate client Dispose
        queue.Complete();

        // Allow completion signal to propagate through the pipeline stages
        await Task.Delay(TimeSpan.FromMilliseconds(500), TestContext.Current.CancellationToken);

        // Pipeline must not have faulted; clean shutdown only
        Assert.False(pipelineTask.IsFaulted,
            "Pipeline must not fault after queue.Complete()");
    }
}