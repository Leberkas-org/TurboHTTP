using System.Collections.Immutable;
using System.Net;
using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHTTP.Protocol.Semantics;
using TurboHTTP.Streams.Stages.Features;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.Tests.Semantics.Stages;

public sealed class ExpectContinueSpec : StreamTestBase
{
    private Task<IImmutableList<HttpRequestMessage>> RunRequestAsync(
        ExpectContinueBidiStage stage,
        params HttpRequestMessage[] requests)
    {
        var graph = GraphDsl.Create(
            Sink.Seq<HttpRequestMessage>(),
            (builder, sink) =>
            {
                var bidi = builder.Add(stage);
                var source = builder.Add(Source.From(requests));
                var emptyResponseSource = builder.Add(Source.Empty<HttpResponseMessage>());
                var ignoredSink = builder.Add(Sink.Ignore<HttpResponseMessage>());

                builder.From(source).To(bidi.Inlet1);
                builder.From(bidi.Outlet1).To(sink);
                builder.From(emptyResponseSource).To(bidi.Inlet2);
                builder.From(bidi.Outlet2).To(ignoredSink);

                return ClosedShape.Instance;
            });

        return RunnableGraph.FromGraph(graph).Run(Materializer);
    }

    private Task<IImmutableList<HttpResponseMessage>> RunFullFlowAsync(
        ExpectContinueBidiStage stage,
        HttpRequestMessage request,
        params HttpResponseMessage[] responses)
    {
        var graph = GraphDsl.Create(
            Sink.Seq<HttpResponseMessage>(),
            (builder, sink) =>
            {
                var bidi = builder.Add(stage);
                var requestSource = builder.Add(Source.Single(request));
                var responseSource = builder.Add(Source.From(responses));
                var ignoredSink = builder.Add(Sink.Ignore<HttpRequestMessage>());

                builder.From(requestSource).To(bidi.Inlet1);
                builder.From(bidi.Outlet1).To(ignoredSink);
                builder.From(responseSource).To(bidi.Inlet2);
                builder.From(bidi.Outlet2).To(sink);

                return ClosedShape.Instance;
            });

        return RunnableGraph.FromGraph(graph).Run(Materializer);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-10.1.1")]
    public async Task ExpectContinue_should_release_body_when_100_continue()
    {
        var policy = new Expect100Policy { MinBodySizeBytes = 100 };
        var stage = new ExpectContinueBidiStage(policy);

        using var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/upload");
        request.Content = new ByteArrayContent(new byte[200]);
        request.Content.Headers.ContentLength = 200;

        // Server sends 100 Continue followed by the final 200 OK
        using var continue100 = new HttpResponseMessage(HttpStatusCode.Continue);
        using var finalResponse = new HttpResponseMessage(HttpStatusCode.OK);

        var results = await RunFullFlowAsync(stage, request, continue100, finalResponse);

        // Only the final response should emerge — 100 is consumed internally
        Assert.Single(results);
        Assert.Equal(HttpStatusCode.OK, results[0].StatusCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-10.1.1")]
    public async Task ExpectContinue_should_cancel_when_417_expectation_failed()
    {
        var policy = new Expect100Policy { MinBodySizeBytes = 100 };
        var stage = new ExpectContinueBidiStage(policy);

        using var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/upload");
        request.Content = new ByteArrayContent(new byte[200]);
        request.Content.Headers.ContentLength = 200;

        using var response417 = new HttpResponseMessage((HttpStatusCode)417);

        var results = await RunFullFlowAsync(stage, request, response417);

        // 417 should be forwarded to the caller
        Assert.Single(results);
        Assert.Equal((HttpStatusCode)417, results[0].StatusCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-10.1.1")]
    public async Task ExpectContinue_should_pass_through_when_null_policy()
    {
        var stage = new ExpectContinueBidiStage();

        using var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/upload");
        request.Content = new ByteArrayContent(new byte[200]);
        request.Content.Headers.ContentLength = 200;

        var results = await RunRequestAsync(stage, request);

        // Request passes through unchanged — no Expect header
        Assert.Single(results);
        Assert.Null(results[0].Headers.ExpectContinue);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-10.1.1")]
    public async Task ExpectContinue_should_add_expect_header_when_body_exceeds_threshold()
    {
        var policy = new Expect100Policy { MinBodySizeBytes = 100 };
        var stage = new ExpectContinueBidiStage(policy);

        using var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/upload");
        request.Content = new ByteArrayContent(new byte[200]);
        request.Content.Headers.ContentLength = 200;

        var results = await RunRequestAsync(stage, request);

        Assert.Single(results);
        Assert.True(results[0].Headers.ExpectContinue);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-10.1.1")]
    public async Task ExpectContinue_should_not_add_expect_header_when_body_below_threshold()
    {
        var policy = new Expect100Policy { MinBodySizeBytes = 1024 };
        var stage = new ExpectContinueBidiStage(policy);

        using var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/upload");
        request.Content = new ByteArrayContent(new byte[50]);
        request.Content.Headers.ContentLength = 50;

        var results = await RunRequestAsync(stage, request);

        Assert.Single(results);
        Assert.NotEqual(true, results[0].Headers.ExpectContinue);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-10.1.1")]
    public async Task ExpectContinue_should_forward_final_response_when_no_expect_pending()
    {
        var policy = new Expect100Policy { MinBodySizeBytes = 1024 };
        var stage = new ExpectContinueBidiStage(policy);

        // Small body — no Expect header
        using var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");

        using var response = new HttpResponseMessage(HttpStatusCode.OK);

        var results = await RunFullFlowAsync(stage, request, response);

        Assert.Single(results);
        Assert.Equal(HttpStatusCode.OK, results[0].StatusCode);
    }
}
