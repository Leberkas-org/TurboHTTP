using System.Net;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using TurboHTTP.Internal;
using TurboHTTP.Streams.Stages.Routing;

namespace TurboHTTP.StreamTests.Http11;

/// <summary>
/// Tests strict one-request-in-flight back-pressure in Http11CorrelationStage per RFC 9112.
/// Verifies that the stage gates InRequest to allow only one request in flight at a time,
/// and only pulls another request after the previous response has been delivered.
/// </summary>
/// <remarks>
/// Stage under test: <see cref="Http11CorrelationStage"/>.
/// RFC 9112 §9.3: HTTP/1.x strict request-response ordering with one-request-in-flight guarantee.
/// </remarks>
public sealed class Http1XCorrelationBackPressureSpec : StreamTestBase
{
    private static HttpResponseMessage OkResponse()
        => new(HttpStatusCode.OK);

    /// <summary>
    /// Creates manual probes for request, response, and two outlets (OutResponse, OutControl).
    /// Returns (requestProbe, responseProbe, responseOut, signalOut).
    /// </summary>
    private (
        TestPublisher.ManualProbe<HttpRequestMessage> RequestProbe,
        TestPublisher.ManualProbe<HttpResponseMessage> ResponseProbe,
        TestSubscriber.ManualProbe<HttpResponseMessage> ResponseOut,
        TestSubscriber.ManualProbe<IOutputItem> SignalOut)
        CreateProbes()
    {
        var requestProbe = this.CreateManualPublisherProbe<HttpRequestMessage>();
        var responseProbe = this.CreateManualPublisherProbe<HttpResponseMessage>();
        var responseOut = this.CreateManualSubscriberProbe<HttpResponseMessage>();
        var signalOut = this.CreateManualSubscriberProbe<IOutputItem>();

        var graph = RunnableGraph.FromGraph(
            GraphDsl.Create(
                (b) =>
                {
                    var stage = b.Add(new Http11CorrelationStage());
                    var reqSrc = b.Add(Source.FromPublisher(requestProbe));
                    var resSrc = b.Add(Source.FromPublisher(responseProbe));

                    b.From(reqSrc).To(stage.InRequest);
                    b.From(resSrc).To(stage.InResponse);
                    b.From(stage.OutResponse).To(Sink.FromSubscriber(responseOut));
                    b.From(stage.OutControl).To(Sink.FromSubscriber(signalOut));

                    return ClosedShape.Instance;
                }));

        graph.Run(Materializer);

        return (requestProbe, responseProbe, responseOut, signalOut);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9.3")]
    public async Task Http1XCorrelationBackPressure_should_maintain_fifo_order_when_three_requests_serial_send()
    {
        var (requestProbe, responseProbe, responseOut, signalOut) = CreateProbes();

        var responseOutSub = responseOut.ExpectSubscription(TestContext.Current.CancellationToken);
        var signalOutSub = signalOut.ExpectSubscription(TestContext.Current.CancellationToken);

        var requestPubSub = requestProbe.ExpectSubscription(TestContext.Current.CancellationToken);
        var responsePubSub = responseProbe.ExpectSubscription(TestContext.Current.CancellationToken);

        // Request demand: 1 on OutResponse + high on OutControl
        responseOutSub.Request(1);
        signalOutSub.Request(100);

        // Build requests and responses
        var request1 = new HttpRequestMessage(HttpMethod.Get, "http://example.com/1");
        var request2 = new HttpRequestMessage(HttpMethod.Get, "http://example.com/2");
        var request3 = new HttpRequestMessage(HttpMethod.Get, "http://example.com/3");
        var response1 = OkResponse();
        var response2 = OkResponse();
        var response3 = OkResponse();

        // Cycle 1: Request1
        requestPubSub.SendNext(request1);
        var sig1 = signalOut.ExpectNext(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        Assert.IsType<StreamAcquireItem>(sig1);

        responsePubSub.SendNext(response1);
        var reuse1 = signalOut.ExpectNext(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        Assert.IsType<ConnectionReuseItem>(reuse1);
        var resp1 = responseOut.ExpectNext(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        Assert.Same(request1, resp1.RequestMessage);
        Assert.Same(response1, resp1);

        // Request more on OutResponse for next response
        responseOutSub.Request(1);

        // Cycle 2: Request2
        requestPubSub.SendNext(request2);
        var sig2 = signalOut.ExpectNext(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        Assert.IsType<StreamAcquireItem>(sig2);

        responsePubSub.SendNext(response2);
        var reuse2 = signalOut.ExpectNext(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        Assert.IsType<ConnectionReuseItem>(reuse2);
        var resp2 = responseOut.ExpectNext(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        Assert.Same(request2, resp2.RequestMessage);
        Assert.Same(response2, resp2);

        // Request more on OutResponse for next response
        responseOutSub.Request(1);

        // Cycle 3: Request3
        requestPubSub.SendNext(request3);
        var sig3 = signalOut.ExpectNext(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        Assert.IsType<StreamAcquireItem>(sig3);

        responsePubSub.SendNext(response3);
        var reuse3 = signalOut.ExpectNext(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        Assert.IsType<ConnectionReuseItem>(reuse3);
        var resp3 = responseOut.ExpectNext(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        Assert.Same(request3, resp3.RequestMessage);
        Assert.Same(response3, resp3);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9.3")]
    public async Task Http1XCorrelationBackPressure_should_pipeline_both_requests_within_depth_limit()
    {
        var (requestProbe, responseProbe, responseOut, signalOut) = CreateProbes();

        var responseOutSub = responseOut.ExpectSubscription(TestContext.Current.CancellationToken);
        var signalOutSub = signalOut.ExpectSubscription(TestContext.Current.CancellationToken);

        var requestPubSub = requestProbe.ExpectSubscription(TestContext.Current.CancellationToken);
        var responsePubSub = responseProbe.ExpectSubscription(TestContext.Current.CancellationToken);

        // Request 2 items upfront on both outlets to allow flow
        responseOutSub.Request(2);
        signalOutSub.Request(100);

        var request1 = new HttpRequestMessage(HttpMethod.Get, "http://example.com/1");
        var request2 = new HttpRequestMessage(HttpMethod.Get, "http://example.com/2");
        var response1 = OkResponse();
        var response2 = OkResponse();

        // Send both requests (queue them at the source)
        requestPubSub.SendNext(request1);
        requestPubSub.SendNext(request2);

        // With pipelining, both requests flow immediately (within pipeline depth of 8).
        // Both StreamAcquireItems are emitted without waiting for responses.
        var sig1 = signalOut.ExpectNext(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        Assert.IsType<StreamAcquireItem>(sig1);

        var sig2 = signalOut.ExpectNext(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        Assert.IsType<StreamAcquireItem>(sig2);

        // Now send responses — FIFO order matches requests
        responsePubSub.SendNext(response1);
        var resp1 = responseOut.ExpectNext(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        Assert.Same(request1, resp1.RequestMessage);

        responsePubSub.SendNext(response2);
        var resp2 = responseOut.ExpectNext(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        Assert.Same(request2, resp2.RequestMessage);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9.3")]
    public async Task Http1XCorrelationBackPressure_should_not_complete_until_in_flight_response_arrives_when_request_completes()
    {
        var (requestProbe, responseProbe, responseOut, signalOut) = CreateProbes();

        var responseOutSub = responseOut.ExpectSubscription(TestContext.Current.CancellationToken);
        var signalOutSub = signalOut.ExpectSubscription(TestContext.Current.CancellationToken);

        var requestPubSub = requestProbe.ExpectSubscription(TestContext.Current.CancellationToken);
        var responsePubSub = responseProbe.ExpectSubscription(TestContext.Current.CancellationToken);

        responseOutSub.Request(1);
        signalOutSub.Request(100);

        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        var response = OkResponse();

        // Send 1 request
        requestPubSub.SendNext(request);
        var sig = signalOut.ExpectNext(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        Assert.IsType<StreamAcquireItem>(sig);

        // Complete the request upstream BEFORE response arrives
        requestPubSub.SendComplete();

        // Stage must NOT complete prematurely — response is still in flight
        responseOut.ExpectNoMsg(TimeSpan.FromMilliseconds(300), TestContext.Current.CancellationToken);

        // Now send the response
        responsePubSub.SendNext(response);
        var resp = responseOut.ExpectNext(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        Assert.Same(request, resp.RequestMessage);

        // Complete response upstream
        responsePubSub.SendComplete();

        // Stage should now complete (both upstreams done, no in-flight request)
        responseOut.ExpectComplete(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9.3")]
    public async Task Http1XCorrelationBackPressure_should_not_pull_response_until_request_in_flight()
    {
        var (requestProbe, responseProbe, responseOut, signalOut) = CreateProbes();

        var responseOutSub = responseOut.ExpectSubscription(TestContext.Current.CancellationToken);
        var signalOutSub = signalOut.ExpectSubscription(TestContext.Current.CancellationToken);

        var requestPubSub = requestProbe.ExpectSubscription(TestContext.Current.CancellationToken);
        var responsePubSub = responseProbe.ExpectSubscription(TestContext.Current.CancellationToken);

        responseOutSub.Request(3);
        signalOutSub.Request(100);

        var request1 = new HttpRequestMessage(HttpMethod.Get, "http://example.com/1");
        var request2 = new HttpRequestMessage(HttpMethod.Get, "http://example.com/2");
        var response1 = OkResponse();
        var response2 = OkResponse();

        // Send request1
        requestPubSub.SendNext(request1);
        var sig1 = signalOut.ExpectNext(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        Assert.IsType<StreamAcquireItem>(sig1);

        // Send response1
        responsePubSub.SendNext(response1);
        var reuse1 = signalOut.ExpectNext(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        Assert.IsType<ConnectionReuseItem>(reuse1);
        var resp1 = responseOut.ExpectNext(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        Assert.Same(request1, resp1.RequestMessage);

        // Now _inFlightRequest == null. Queue a second response (even though stage has no request in flight)
        responsePubSub.SendNext(response2);

        // Stage should NOT pull response2 yet because no request is in flight
        // Verify responseOut does not receive response2 within the gate period
        responseOut.ExpectNoMsg(TimeSpan.FromMilliseconds(300), TestContext.Current.CancellationToken);

        // Now send request2 — this should pull the queued response2 from upstream
        requestPubSub.SendNext(request2);
        var sig2 = signalOut.ExpectNext(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        Assert.IsType<StreamAcquireItem>(sig2);

        // The excess response2 is now pulled and delivered
        var resp2 = responseOut.ExpectNext(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        Assert.Same(request2, resp2.RequestMessage);
        Assert.Same(response2, resp2);
    }
}
