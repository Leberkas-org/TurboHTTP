using System.Collections.Immutable;
using System.Net;
using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHTTP.Protocol.Semantics;
using TurboHTTP.Streams.Stages.Features;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.StreamTests.Semantics;

/// <summary>
/// Tests the Expect: 100-continue bidirectional stage per RFC 9110 §10.1.1.
/// Verifies that the request direction adds the Expect header for large bodies,
/// and the response direction filters 100 Continue and forwards 417 responses.
/// </summary>
/// <remarks>
/// Stage under test: <see cref="ExpectContinueBidiStage"/>.
/// </remarks>
public sealed class ExpectContinueSpec : StreamTestBase
{
    /// <summary>
    /// Runs requests through the request direction (In1→Out1) of the BidiStage.
    /// The response direction is wired to empty source / ignored sink.
    /// </summary>
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

    /// <summary>
    /// Runs a single request through the BidiStage and feeds back specified responses.
    /// Collects responses that emerge from Out2.
    /// </summary>
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
    public async Task Should_ReleaseBody_When_100Continue()
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
    public async Task Should_Cancel_When_417ExpectationFailed()
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
    public async Task Should_PassThrough_When_NullPolicy()
    {
        var stage = new ExpectContinueBidiStage(null);

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
    public async Task Should_AddExpectHeader_When_BodyExceedsThreshold()
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
    public async Task Should_NotAddExpectHeader_When_BodyBelowThreshold()
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
    public async Task Should_ForwardFinalResponse_When_NoExpectPending()
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
