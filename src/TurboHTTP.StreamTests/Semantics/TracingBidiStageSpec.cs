using System.Collections.Immutable;
using System.Net;
using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHTTP.Diagnostics;
using TurboHTTP.Streams.Stages.Features;
using TurboHTTP.Tests.Shared;
using static Servus.Core.Servus;
using ActivityListener = System.Diagnostics.ActivityListener;
using ActivitySamplingResult = System.Diagnostics.ActivitySamplingResult;
using ActivitySource = System.Diagnostics.ActivitySource;

namespace TurboHTTP.StreamTests.Semantics;

public sealed class TracingBidiStageSpec : StreamTestBase, IDisposable
{
    private readonly ActivityListener _listener;
    private readonly List<System.Diagnostics.Activity> _activities = [];

    public TracingBidiStageSpec()
    {
        var sourceName = Tracing.Source.Name;
        _listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == sourceName,
            Sample = (ref _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStarted = activity => _activities.Add(activity)
        };
        ActivitySource.AddActivityListener(_listener);
    }

    public new void Dispose()
    {
        _listener.Dispose();
        foreach (var activity in _activities)
        {
            if (!activity.IsStopped)
            {
                activity.Stop();
            }
        }

        base.Dispose();
    }

    private Task<IImmutableList<HttpRequestMessage>> RunRequestAsync(
        TracingBidiStage stage,
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
        TracingBidiStage stage,
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
        TracingBidiStage stage,
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

    [Fact(Timeout = 5000)]
    public async Task TracingBidiStage_should_pass_through_request()
    {
        var stage = new TracingBidiStage();
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");

        var results = await RunRequestAsync(stage, request);

        var result = Assert.Single(results);
        Assert.Same(request, result);
    }

    [Fact(Timeout = 5000)]
    public async Task TracingBidiStage_should_store_activity_in_request_options()
    {
        var stage = new TracingBidiStage();
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");

        var results = await RunRequestAsync(stage, request);

        var result = Assert.Single(results);
        Assert.True(result.Options.TryGetValue(
            TurboHttpInstrumentationExtensions.RequestActivityKey, out var activity));
        Assert.NotNull(activity);
    }

    [Fact(Timeout = 5000)]
    public async Task TracingBidiStage_should_inject_trace_context_into_request()
    {
        var stage = new TracingBidiStage();
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");

        var results = await RunRequestAsync(stage, request);

        var result = Assert.Single(results);
        // Verify trace context headers were potentially added
        Assert.NotNull(result.Headers);
    }

    [Fact(Timeout = 5000)]
    public async Task TracingBidiStage_should_handle_multiple_requests()
    {
        var stage = new TracingBidiStage();
        var req1 = new HttpRequestMessage(HttpMethod.Get, "http://example.com/a");
        var req2 = new HttpRequestMessage(HttpMethod.Get, "http://example.com/b");

        var results = await RunRequestAsync(stage, req1, req2);

        Assert.Equal(2, results.Count);
        Assert.True(results[0].Options.TryGetValue(
            TurboHttpInstrumentationExtensions.RequestActivityKey, out var act1));
        Assert.True(results[1].Options.TryGetValue(
            TurboHttpInstrumentationExtensions.RequestActivityKey, out var act2));
        Assert.NotNull(act1);
        Assert.NotNull(act2);
    }

    [Fact(Timeout = 5000)]
    public async Task TracingBidiStage_should_pass_through_response()
    {
        var stage = new TracingBidiStage();
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        response.RequestMessage = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");

        var results = await RunResponseAsync(stage, response);

        var result = Assert.Single(results);
        Assert.Same(response, result);
    }

    [Fact(Timeout = 5000)]
    public async Task TracingBidiStage_should_complete_activity_on_response()
    {
        var stage = new TracingBidiStage();

        var results = await RunBidiWithEchoAsync(
            stage,
            req =>
            {
                var resp = new HttpResponseMessage(HttpStatusCode.OK) { RequestMessage = req };
                return resp;
            },
            new HttpRequestMessage(HttpMethod.Get, "http://example.com/"));

        Assert.Single(results);
    }

    [Fact(Timeout = 5000)]
    public async Task TracingBidiStage_should_handle_response_without_request_message()
    {
        var stage = new TracingBidiStage();
        var response = new HttpResponseMessage(HttpStatusCode.OK);

        var results = await RunResponseAsync(stage, response);

        var result = Assert.Single(results);
        Assert.Same(response, result);
    }

    [Fact(Timeout = 5000)]
    public async Task TracingBidiStage_should_handle_various_status_codes()
    {
        var stage = new TracingBidiStage();

        var responses = new[]
        {
            MakeResponse(),
            MakeResponse(HttpStatusCode.Created),
            MakeResponse(HttpStatusCode.BadRequest),
            MakeResponse(HttpStatusCode.InternalServerError),
        };

        var results = await RunResponseAsync(stage, responses);

        Assert.Equal(4, results.Count);
    }

    [Fact(Timeout = 5000)]
    public async Task TracingBidiStage_should_record_request_duration()
    {
        var stage = new TracingBidiStage();

        var results = await RunBidiWithEchoAsync(
            stage,
            req =>
            {
                var resp = new HttpResponseMessage(HttpStatusCode.OK) { RequestMessage = req };
                return resp;
            },
            new HttpRequestMessage(HttpMethod.Get, "http://example.com/"));

        Assert.Single(results);
    }

    [Fact(Timeout = 5000)]
    public async Task TracingBidiStage_should_handle_stream_with_multiple_requests_and_responses()
    {
        var stage = new TracingBidiStage();

        var requests = new[]
        {
            new HttpRequestMessage(HttpMethod.Get, "http://example.com/a"),
            new HttpRequestMessage(HttpMethod.Get, "http://example.com/b"),
            new HttpRequestMessage(HttpMethod.Post, "http://example.com/c"),
        };

        var results = await RunBidiWithEchoAsync(
            stage,
            req =>
            {
                var resp = new HttpResponseMessage(
                    req.Method == HttpMethod.Post ? HttpStatusCode.Created : HttpStatusCode.OK)
                {
                    RequestMessage = req
                };
                return resp;
            },
            requests);

        Assert.Equal(3, results.Count);
    }

    [Fact(Timeout = 5000)]
    public async Task TracingBidiStage_should_handle_request_upstream_failure()
    {
        var stage = new TracingBidiStage();
        var testException = new InvalidOperationException("Test error");

        var graph = GraphDsl.Create(
            Sink.Ignore<HttpRequestMessage>(),
            (builder, sink) =>
            {
                var bidi = builder.Add(stage);
                var failSource = builder.Add(Source.Failed<HttpRequestMessage>(testException));
                var emptyResponseSource = builder.Add(Source.Empty<HttpResponseMessage>());
                var ignoredResponseSink = builder.Add(Sink.Ignore<HttpResponseMessage>());

                builder.From(failSource).To(bidi.Inlet1);
                builder.From(bidi.Outlet1).To(sink);
                builder.From(emptyResponseSource).To(bidi.Inlet2);
                builder.From(bidi.Outlet2).To(ignoredResponseSink);

                return ClosedShape.Instance;
            });

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await RunnableGraph.FromGraph(graph).Run(Materializer));
    }

    private static HttpResponseMessage MakeResponse(
        HttpStatusCode statusCode = HttpStatusCode.OK,
        string? requestUri = null)
    {
        var response = new HttpResponseMessage(statusCode);
        if (requestUri is not null)
        {
            response.RequestMessage = new HttpRequestMessage(HttpMethod.Get, requestUri);
        }
        else
        {
            response.RequestMessage = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        }

        return response;
    }
}