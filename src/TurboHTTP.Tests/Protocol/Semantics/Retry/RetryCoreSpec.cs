using System.Collections.Immutable;
using System.Net;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using TurboHTTP.Protocol.Semantics;
using TurboHTTP.Streams.Stages.Features;
using TurboHTTP.Tests.Shared;
using Microsoft.AspNetCore.Http.Features;

namespace TurboHTTP.Tests.Protocol.Semantics.Retry;

public sealed class RetryCoreSpec : StreamTestBase
{
    private static readonly HttpRequestOptionsKey<int> AttemptCountKey = new("TurboHTTP.RetryAttemptCount");

    private Task<IImmutableList<HttpRequestMessage>> RunRequestAsync(
        RetryBidiStage stage,
        params HttpRequestMessage[] requests)
    {
        var graph = GraphDsl.Create(
            Sink.Seq<HttpRequestMessage>(),
            (builder, sink) =>
            {
                var bidi = builder.Add(stage);
                var source = builder.Add(Source.From(requests));
                var neverResponseSource = builder.Add(Source.Maybe<HttpResponseMessage>());
                var ignoredSink = builder.Add(Sink.Ignore<HttpResponseMessage>());

                var take = builder.Add(Flow.Create<HttpRequestMessage>().Take(requests.Length));

                builder.From(source).To(bidi.Inlet1);
                builder.From(bidi.Outlet1).Via(take).To(sink);
                builder.From(neverResponseSource).To(bidi.Inlet2);
                builder.From(bidi.Outlet2).To(ignoredSink);

                return ClosedShape.Instance;
            });

        return RunnableGraph.FromGraph(graph).Run(Materializer);
    }

    private Task<IImmutableList<HttpResponseMessage>> RunResponseAsync(
        RetryBidiStage stage,
        params HttpResponseMessage[] responses)
    {
        var graph = GraphDsl.Create(
            Sink.Seq<HttpResponseMessage>(),
            (builder, sink) =>
            {
                var bidi = builder.Add(stage);
                var source = builder.Add(Source.From(responses));
                var emptyRequestSource = builder.Add(Source.Empty<HttpRequestMessage>());
                var ignoredSink = builder.Add(Sink.Ignore<HttpRequestMessage>());

                builder.From(emptyRequestSource).To(bidi.Inlet1);
                builder.From(bidi.Outlet1).To(ignoredSink);
                builder.From(source).To(bidi.Inlet2);
                builder.From(bidi.Outlet2).To(sink);

                return ClosedShape.Instance;
            });

        return RunnableGraph.FromGraph(graph).Run(Materializer);
    }

    private (TestSubscriber.ManualProbe<HttpRequestMessage> requestOut,
        TestSubscriber.ManualProbe<HttpResponseMessage> responseOut,
        Action<HttpResponseMessage> pushResponse) RunManual(
            RetryBidiStage stage,
            int requestOutDemand,
            int responseOutDemand,
            params HttpRequestMessage[] requests)
    {
        var responsePublisher = this.CreateManualPublisherProbe<HttpResponseMessage>();
        var requestOutProbe = this.CreateManualSubscriberProbe<HttpRequestMessage>();
        var responseOutProbe = this.CreateManualSubscriberProbe<HttpResponseMessage>();

        RunnableGraph.FromGraph(GraphDsl.Create(b =>
        {
            var bidi = b.Add(stage);
            var reqSrc = b.Add(Source.From(requests).Concat(Source.Never<HttpRequestMessage>()));
            var respSrc = b.Add(Source.FromPublisher(responsePublisher));

            b.From(reqSrc).To(bidi.Inlet1);
            b.From(bidi.Outlet1).To(Sink.FromSubscriber(requestOutProbe));
            b.From(respSrc).To(bidi.Inlet2);
            b.From(bidi.Outlet2).To(Sink.FromSubscriber(responseOutProbe));

            return ClosedShape.Instance;
        })).Run(Materializer);

        var responseSub = responsePublisher.ExpectSubscription(TestContext.Current.CancellationToken);
        var reqOutSub = requestOutProbe.ExpectSubscription(TestContext.Current.CancellationToken);
        var respOutSub = responseOutProbe.ExpectSubscription(TestContext.Current.CancellationToken);

        reqOutSub.Request(requestOutDemand);
        respOutSub.Request(responseOutDemand);

        return (requestOutProbe, responseOutProbe, responseSub.SendNext);
    }

    private static HttpResponseMessage BuildResponse(
        HttpStatusCode statusCode,
        HttpRequestMessage? requestMessage = null,
        string? retryAfterSeconds = null)
    {
        var response = new HttpResponseMessage(statusCode)
        {
            RequestMessage = requestMessage ?? new HttpRequestMessage(HttpMethod.Get, "http://example.com/resource")
        };
        if (retryAfterSeconds is not null)
        {
            response.Headers.TryAddWithoutValidation("Retry-After", retryAfterSeconds);
        }

        return response;
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9110-9.2")]
    public async Task RequestDirection_should_pass_through_when_policy_is_null()
    {
        var stage = new RetryBidiStage();
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");

        var results = await RunRequestAsync(stage, request);

        var result = Assert.Single(results);
        Assert.Same(request, result);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9110-9.2")]
    public async Task ResponseDirection_should_pass_through_when_policy_is_null()
    {
        var stage = new RetryBidiStage();
        var response = BuildResponse(HttpStatusCode.ServiceUnavailable);

        var results = await RunResponseAsync(stage, response);

        var result = Assert.Single(results);
        Assert.Same(response, result);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9110-9.2")]
    public async Task RequestDirection_should_forward_request()
    {
        var stage = new RetryBidiStage(new RetryPolicy());
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");

        var results = await RunRequestAsync(stage, request);

        var result = Assert.Single(results);
        Assert.Same(request, result);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9110-9.2")]
    public async Task RequestDirection_should_forward_multiple_requests_in_order()
    {
        var stage = new RetryBidiStage(new RetryPolicy());
        var req1 = new HttpRequestMessage(HttpMethod.Get, "http://example.com/a");
        var req2 = new HttpRequestMessage(HttpMethod.Get, "http://example.com/b");

        var results = await RunRequestAsync(stage, req1, req2);

        Assert.Equal(2, results.Count);
        Assert.Same(req1, results[0]);
        Assert.Same(req2, results[1]);
    }

    [Fact]
    [Trait("RFC", "RFC9110-9.2")]
    public void RetryCore_should_forward_final_response_when_200_ok()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        var stage = new RetryBidiStage(new RetryPolicy());
        var (reqOut, respOut, pushResp) = RunManual(stage, 5, 5, request);

        // Consume the forwarded request
        reqOut.ExpectNext(TestContext.Current.CancellationToken);

        // Push a 200 OK response
        var response = BuildResponse(HttpStatusCode.OK, request);
        pushResp(response);

        Assert.Same(response, respOut.ExpectNext(TestContext.Current.CancellationToken));
    }

    [Fact]
    [Trait("RFC", "RFC9110-9.2")]
    public void RetryCore_should_forward_final_response_when_404()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        var stage = new RetryBidiStage(new RetryPolicy());
        var (reqOut, respOut, pushResp) = RunManual(stage, 5, 5, request);

        reqOut.ExpectNext(TestContext.Current.CancellationToken);

        var response = BuildResponse(HttpStatusCode.NotFound, request);
        pushResp(response);

        Assert.Same(response, respOut.ExpectNext(TestContext.Current.CancellationToken));
    }

    [Fact]
    [Trait("RFC", "RFC9110-9.2")]
    public void RetryCore_should_forward_final_response_when_post_returns_408()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/");
        var stage = new RetryBidiStage(new RetryPolicy());
        var (reqOut, respOut, pushResp) = RunManual(stage, 5, 5, request);

        reqOut.ExpectNext(TestContext.Current.CancellationToken);

        var response = BuildResponse(HttpStatusCode.RequestTimeout, request);
        pushResp(response);

        Assert.Same(response, respOut.ExpectNext(TestContext.Current.CancellationToken));
    }

    [Fact]
    [Trait("RFC", "RFC9110-9.2")]
    public void RetryCore_should_forward_final_response_when_request_message_is_null()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        var stage = new RetryBidiStage(new RetryPolicy());
        var (reqOut, respOut, pushResp) = RunManual(stage, 5, 5, request);

        reqOut.ExpectNext(TestContext.Current.CancellationToken);

        var response = new HttpResponseMessage(HttpStatusCode.RequestTimeout);
        pushResp(response);

        Assert.Same(response, respOut.ExpectNext(TestContext.Current.CancellationToken));
    }

    [Fact]
    [Trait("RFC", "RFC9110-9.2")]
    public void RetryCore_should_emit_retry_on_out1_when_get_returns_408()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        var stage = new RetryBidiStage(new RetryPolicy());
        var (reqOut, respOut, pushResp) = RunManual(stage, 5, 5, request);

        // Original request forwarded
        Assert.Same(request, reqOut.ExpectNext(TestContext.Current.CancellationToken));

        // 408 response triggers retry
        var response = BuildResponse(HttpStatusCode.RequestTimeout, request);
        pushResp(response);

        // Retry request appears on Out1
        var retryReq = reqOut.ExpectNext(TestContext.Current.CancellationToken);
        Assert.Same(request, retryReq);

        // No response on Out2 (response was disposed)
        respOut.ExpectNoMsg(TimeSpan.FromMilliseconds(100), TestContext.Current.CancellationToken);
    }

    [Fact]
    [Trait("RFC", "RFC9110-9.2")]
    public void RetryCore_should_emit_retry_on_out1_when_get_returns_503()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        var stage = new RetryBidiStage(new RetryPolicy());
        var (reqOut, respOut, pushResp) = RunManual(stage, 5, 5, request);

        Assert.Same(request, reqOut.ExpectNext(TestContext.Current.CancellationToken));

        var response = BuildResponse(HttpStatusCode.ServiceUnavailable, request);
        pushResp(response);

        var retryReq = reqOut.ExpectNext(TestContext.Current.CancellationToken);
        Assert.Same(request, retryReq);
        respOut.ExpectNoMsg(TimeSpan.FromMilliseconds(100), TestContext.Current.CancellationToken);
    }

    [Fact]
    [Trait("RFC", "RFC9110-9.2")]
    public void RetryCore_should_increment_attempt_count_when_retrying()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        var stage = new RetryBidiStage(new RetryPolicy());
        var (reqOut, _, pushResp) = RunManual(stage, 5, 5, request);

        reqOut.ExpectNext(TestContext.Current.CancellationToken);

        var response = BuildResponse(HttpStatusCode.RequestTimeout, request);
        pushResp(response);

        var retryReq = reqOut.ExpectNext(TestContext.Current.CancellationToken);
        Assert.True(retryReq.Options.TryGetValue(AttemptCountKey, out var count));
        Assert.Equal(2, count);
    }

    [Fact]
    [Trait("RFC", "RFC9110-9.2")]
    public void RetryCore_should_forward_final_response_when_retry_limit_reached()
    {
        var policy = new RetryPolicy { MaxRetries = 1 };
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        var stage = new RetryBidiStage(policy);
        var (reqOut, respOut, pushResp) = RunManual(stage, 5, 5, request);

        reqOut.ExpectNext(TestContext.Current.CancellationToken);

        // MaxRetries=1, attemptCount starts at 1 → 1 >= 1 → no retry
        var response = BuildResponse(HttpStatusCode.RequestTimeout, request);
        pushResp(response);

        Assert.Same(response, respOut.ExpectNext(TestContext.Current.CancellationToken));
        reqOut.ExpectNoMsg(TimeSpan.FromMilliseconds(100), TestContext.Current.CancellationToken);
    }

    [Theory]
    [Trait("RFC", "RFC9110-9.2")]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    [InlineData("HEAD")]
    [InlineData("OPTIONS")]
    public void RetryCore_should_retry_on_408_when_method_is_idempotent(string methodName)
    {
        var method = new HttpMethod(methodName);
        var request = new HttpRequestMessage(method, "http://example.com/");
        var stage = new RetryBidiStage(new RetryPolicy());
        var (reqOut, respOut, pushResp) = RunManual(stage, 5, 5, request);

        reqOut.ExpectNext(TestContext.Current.CancellationToken);

        var response = BuildResponse(HttpStatusCode.RequestTimeout, request);
        pushResp(response);

        var retryReq = reqOut.ExpectNext(TestContext.Current.CancellationToken);
        Assert.Equal(method, retryReq.Method);
        respOut.ExpectNoMsg(TimeSpan.FromMilliseconds(100), TestContext.Current.CancellationToken);
    }

    [Fact]
    [Trait("RFC", "RFC9110-9.2")]
    public void RetryCore_should_forward_on_out2_when_patch_returns_503()
    {
        var request = new HttpRequestMessage(HttpMethod.Patch, "http://example.com/");
        var stage = new RetryBidiStage(new RetryPolicy());
        var (reqOut, respOut, pushResp) = RunManual(stage, 5, 5, request);

        reqOut.ExpectNext(TestContext.Current.CancellationToken);

        var response = BuildResponse(HttpStatusCode.ServiceUnavailable, request);
        pushResp(response);

        Assert.Same(response, respOut.ExpectNext(TestContext.Current.CancellationToken));
    }

    [Theory]
    [Trait("RFC", "RFC9110-9.2")]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void RetryCore_should_retry_exactly_max_retries_minus_one_times_then_forward_response(int maxRetries)
    {
        var policy = new RetryPolicy { MaxRetries = maxRetries };
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        var stage = new RetryBidiStage(policy);
        var (reqOut, respOut, pushResp) = RunManual(stage, maxRetries + 2, maxRetries + 2, request);

        reqOut.ExpectNext(TestContext.Current.CancellationToken);

        for (var attempt = 1; attempt < maxRetries; attempt++)
        {
            var retryResponse = BuildResponse(HttpStatusCode.ServiceUnavailable, request);
            pushResp(retryResponse);

            var retryReq = reqOut.ExpectNext(TestContext.Current.CancellationToken);
            Assert.Same(request, retryReq);
        }

        var finalResponse = BuildResponse(HttpStatusCode.ServiceUnavailable, request);
        pushResp(finalResponse);

        Assert.Same(finalResponse, respOut.ExpectNext(TestContext.Current.CancellationToken));
        reqOut.ExpectNoMsg(TimeSpan.FromMilliseconds(100), TestContext.Current.CancellationToken);
    }
}
