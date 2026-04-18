using System.Collections.Immutable;
using System.Net;
using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHTTP.Protocol.Semantics;
using TurboHTTP.Streams.Stages.Features;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.StreamTests.Semantics;

public sealed class ExpectContinueBidiStageSpec : StreamTestBase
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
                var ignoredResponseSink = builder.Add(Sink.Ignore<HttpResponseMessage>());

                builder.From(source).To(bidi.Inlet1);
                builder.From(bidi.Outlet1).To(sink);
                builder.From(emptyResponseSource).To(bidi.Inlet2);
                builder.From(bidi.Outlet2).To(ignoredResponseSink);

                return ClosedShape.Instance;
            });

        return RunnableGraph.FromGraph(graph).Run(Materializer);
    }

    private Task<IImmutableList<HttpResponseMessage>> RunResponseAsync(
        ExpectContinueBidiStage stage,
        params HttpResponseMessage[] responses)
    {
        var graph = GraphDsl.Create(
            Sink.Seq<HttpResponseMessage>(),
            (builder, sink) =>
            {
                var bidi = builder.Add(stage);
                var source = builder.Add(Source.From(responses));
                var emptyRequestSource = builder.Add(Source.Empty<HttpRequestMessage>());
                var ignoredRequestSink = builder.Add(Sink.Ignore<HttpRequestMessage>());

                builder.From(emptyRequestSource).To(bidi.Inlet1);
                builder.From(bidi.Outlet1).To(ignoredRequestSink);
                builder.From(source).To(bidi.Inlet2);
                builder.From(bidi.Outlet2).To(sink);

                return ClosedShape.Instance;
            });

        return RunnableGraph.FromGraph(graph).Run(Materializer);
    }

    private Task<IImmutableList<HttpResponseMessage>> RunBidiWithEchoAsync(
        ExpectContinueBidiStage stage,
        Func<HttpRequestMessage, HttpResponseMessage> echoFn,
        params HttpRequestMessage[] requests)
    {
        var graph = GraphDsl.Create(
            Sink.Seq<HttpResponseMessage>(),
            (builder, sink) =>
            {
                var bidi = builder.Add(stage);
                var source = builder.Add(Source.From(requests));
                var echo = builder.Add(Flow.Create<HttpRequestMessage>().Select(echoFn));

                builder.From(source).To(bidi.Inlet1);
                builder.From(bidi.Outlet1).Via(echo).To(bidi.Inlet2);
                builder.From(bidi.Outlet2).To(sink);

                return ClosedShape.Instance;
            });

        return RunnableGraph.FromGraph(graph).Run(Materializer);
    }

    [Trait("RFC", "RFC9110-10.1.1")]
    [Fact(Timeout = 5000)]
    public async Task ExpectContinueBidiStage_should_pass_through_when_policy_is_null()
    {
        var stage = new ExpectContinueBidiStage();
        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/")
        {
            Content = new ByteArrayContent(new byte[10000])
        };

        var results = await RunRequestAsync(stage, request);

        var result = Assert.Single(results);
        Assert.False(result.Headers.ExpectContinue.GetValueOrDefault());
    }

    [Trait("RFC", "RFC9110-10.1.1")]
    [Fact(Timeout = 5000)]
    public async Task ExpectContinueBidiStage_should_add_expect_header_when_body_exceeds_threshold()
    {
        var policy = new Expect100Policy { MinBodySizeBytes = 1000 };
        var stage = new ExpectContinueBidiStage(policy);
        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/")
        {
            Content = new ByteArrayContent(new byte[2000])
        };

        var results = await RunRequestAsync(stage, request);

        var result = Assert.Single(results);
        Assert.True(result.Headers.ExpectContinue.GetValueOrDefault());
    }

    [Trait("RFC", "RFC9110-10.1.1")]
    [Fact(Timeout = 5000)]
    public async Task ExpectContinueBidiStage_should_not_add_expect_header_when_body_below_threshold()
    {
        var policy = new Expect100Policy { MinBodySizeBytes = 1000 };
        var stage = new ExpectContinueBidiStage(policy);
        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/")
        {
            Content = new ByteArrayContent(new byte[500])
        };

        var results = await RunRequestAsync(stage, request);

        var result = Assert.Single(results);
        Assert.False(result.Headers.ExpectContinue.GetValueOrDefault());
    }

    [Trait("RFC", "RFC9110-10.1.1")]
    [Fact(Timeout = 5000)]
    public async Task ExpectContinueBidiStage_should_not_add_expect_header_when_no_body()
    {
        var policy = new Expect100Policy { MinBodySizeBytes = 1000 };
        var stage = new ExpectContinueBidiStage(policy);
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");

        var results = await RunRequestAsync(stage, request);

        var result = Assert.Single(results);
        Assert.False(result.Headers.ExpectContinue.GetValueOrDefault());
    }

    [Trait("RFC", "RFC9110-10.1.1")]
    [Fact(Timeout = 5000)]
    public async Task ExpectContinueBidiStage_should_pass_through_get_requests()
    {
        var policy = new Expect100Policy { MinBodySizeBytes = 1000 };
        var stage = new ExpectContinueBidiStage(policy);
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");

        var results = await RunRequestAsync(stage, request);

        var result = Assert.Single(results);
        Assert.Same(request, result);
    }

    [Trait("RFC", "RFC9110-10.1.1")]
    [Fact(Timeout = 5000)]
    public async Task ExpectContinueBidiStage_should_consume_100_continue_response()
    {
        var policy = new Expect100Policy { MinBodySizeBytes = 1000 };
        var stage = new ExpectContinueBidiStage(policy);

        var results = await RunBidiWithEchoAsync(
            stage,
            req =>
            {
                if (req.Headers.ExpectContinue.GetValueOrDefault())
                {
                    // First push 100 Continue (which should be consumed)
                    // Then push the final response
                    var continueResp = new HttpResponseMessage(HttpStatusCode.Continue)
                    {
                        RequestMessage = req
                    };
                    return continueResp;
                }

                return new HttpResponseMessage(HttpStatusCode.OK) { RequestMessage = req };
            },
            new HttpRequestMessage(HttpMethod.Post, "http://example.com/")
            {
                Content = new ByteArrayContent(new byte[2000])
            });

        // The 100 Continue should be consumed, not forwarded
        Assert.Empty(results);
    }

    [Trait("RFC", "RFC9110-10.1.1")]
    [Fact(Timeout = 5000)]
    public async Task ExpectContinueBidiStage_should_forward_417_expectation_failed()
    {
        var policy = new Expect100Policy { MinBodySizeBytes = 1000 };
        var stage = new ExpectContinueBidiStage(policy);

        var failedResponse = new HttpResponseMessage(HttpStatusCode.ExpectationFailed);
        failedResponse.RequestMessage = new HttpRequestMessage(HttpMethod.Post, "http://example.com/")
        {
            Content = new ByteArrayContent(new byte[2000])
        };

        var results = await RunResponseAsync(stage, failedResponse);

        var result = Assert.Single(results);
        Assert.Equal(HttpStatusCode.ExpectationFailed, result.StatusCode);
    }

    [Trait("RFC", "RFC9110-10.1.1")]
    [Fact(Timeout = 5000)]
    public async Task ExpectContinueBidiStage_should_forward_final_response_when_expect_pending()
    {
        var policy = new Expect100Policy { MinBodySizeBytes = 1000 };
        var stage = new ExpectContinueBidiStage(policy);

        var finalResponse = new HttpResponseMessage(HttpStatusCode.OK);
        finalResponse.RequestMessage = new HttpRequestMessage(HttpMethod.Post, "http://example.com/")
        {
            Content = new ByteArrayContent(new byte[2000])
        };

        var results = await RunResponseAsync(stage, finalResponse);

        var result = Assert.Single(results);
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
    }

    [Trait("RFC", "RFC9110-10.1.1")]
    [Fact(Timeout = 5000)]
    public async Task ExpectContinueBidiStage_should_pass_through_response_when_no_expect_header()
    {
        var policy = new Expect100Policy { MinBodySizeBytes = 1000 };
        var stage = new ExpectContinueBidiStage(policy);

        var response = new HttpResponseMessage(HttpStatusCode.OK);
        response.RequestMessage = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");

        var results = await RunResponseAsync(stage, response);

        var result = Assert.Single(results);
        Assert.Same(response, result);
    }

    [Trait("RFC", "RFC9110-10.1.1")]
    [Fact(Timeout = 5000)]
    public async Task ExpectContinueBidiStage_should_handle_mixed_expect_and_non_expect_requests()
    {
        var policy = new Expect100Policy { MinBodySizeBytes = 1000 };
        var stage = new ExpectContinueBidiStage(policy);

        var largeBodyRequest = new HttpRequestMessage(HttpMethod.Post, "http://example.com/a")
        {
            Content = new ByteArrayContent(new byte[2000])
        };
        var smallBodyRequest = new HttpRequestMessage(HttpMethod.Post, "http://example.com/b")
        {
            Content = new ByteArrayContent(new byte[500])
        };

        var results = await RunRequestAsync(stage, largeBodyRequest, smallBodyRequest);

        Assert.Equal(2, results.Count);
        Assert.True(results[0].Headers.ExpectContinue.GetValueOrDefault());
        Assert.False(results[1].Headers.ExpectContinue.GetValueOrDefault());
    }

    [Trait("RFC", "RFC9110-10.1.1")]
    [Fact(Timeout = 5000)]
    public async Task ExpectContinueBidiStage_should_handle_response_without_request_message()
    {
        var policy = new Expect100Policy { MinBodySizeBytes = 1000 };
        var stage = new ExpectContinueBidiStage(policy);

        var response = new HttpResponseMessage(HttpStatusCode.OK);

        var results = await RunResponseAsync(stage, response);

        var result = Assert.Single(results);
        Assert.Same(response, result);
    }

    [Trait("RFC", "RFC9110-10.1.1")]
    [Fact(Timeout = 5000)]
    public async Task ExpectContinueBidiStage_should_handle_various_response_codes()
    {
        var policy = new Expect100Policy { MinBodySizeBytes = 1000 };
        var stage = new ExpectContinueBidiStage(policy);

        var responses = new[]
        {
            MakeResponse(HttpStatusCode.OK),
            MakeResponse(HttpStatusCode.Created),
            MakeResponse(HttpStatusCode.BadRequest),
            MakeResponse(HttpStatusCode.InternalServerError),
        };

        var results = await RunResponseAsync(stage, responses);

        Assert.Equal(4, results.Count);
    }

    private static HttpResponseMessage MakeResponse(HttpStatusCode statusCode)
    {
        var response = new HttpResponseMessage(statusCode);
        response.RequestMessage = new HttpRequestMessage(HttpMethod.Post, "http://example.com/")
        {
            Content = new ByteArrayContent(new byte[2000])
        };
        return response;
    }
}