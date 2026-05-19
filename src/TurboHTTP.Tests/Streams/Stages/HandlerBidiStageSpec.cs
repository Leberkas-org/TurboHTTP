using TurboHTTP.Client;
using System.Collections.Immutable;
using Akka;
using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHTTP.Streams.Stages.Features;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.Tests.Streams.Stages;

public sealed class HandlerBidiStageSpec : StreamTestBase
{
    private sealed class RequestHeaderHandler : TurboHandler
    {
        private readonly string _name;
        private readonly string _value;

        public RequestHeaderHandler(string name, string value)
        {
            _name = name;
            _value = value;
        }

        public override HttpRequestMessage ProcessRequest(HttpRequestMessage request)
        {
            request.Headers.TryAddWithoutValidation(_name, _value);
            return request;
        }
    }

    private sealed class ResponseHeaderHandler : TurboHandler
    {
        private readonly string _name;
        private readonly string _value;

        public ResponseHeaderHandler(string name, string value)
        {
            _name = name;
            _value = value;
        }

        public override HttpResponseMessage ProcessResponse(HttpRequestMessage original, HttpResponseMessage response)
        {
            response.Headers.TryAddWithoutValidation(_name, _value);
            return response;
        }
    }

    private Task<IImmutableList<HttpRequestMessage>> RunRequestAsync(
        HandlerBidiStage stage,
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
        HandlerBidiStage stage,
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

    private Task<IImmutableList<HttpRequestMessage>> RunRequestThroughComposedAsync(
        BidiFlow<HttpRequestMessage, HttpRequestMessage, HttpResponseMessage, HttpResponseMessage, NotUsed> bidi,
        params HttpRequestMessage[] requests)
    {
        var graph = GraphDsl.Create(
            Sink.Seq<HttpRequestMessage>(),
            (builder, sink) =>
            {
                var b = builder.Add(bidi);
                var source = builder.Add(Source.From(requests));
                var emptyResponseSource = builder.Add(Source.Empty<HttpResponseMessage>());
                var ignoredResponseSink = builder.Add(Sink.Ignore<HttpResponseMessage>());

                builder.From(source).To(b.Inlet1);
                builder.From(b.Outlet1).To(sink);
                builder.From(emptyResponseSource).To(b.Inlet2);
                builder.From(b.Outlet2).To(ignoredResponseSink);

                return ClosedShape.Instance;
            });

        return RunnableGraph.FromGraph(graph).Run(Materializer);
    }

    private Task<IImmutableList<HttpResponseMessage>> RunResponseThroughComposedAsync(
        BidiFlow<HttpRequestMessage, HttpRequestMessage, HttpResponseMessage, HttpResponseMessage, NotUsed> bidi,
        params HttpResponseMessage[] responses)
    {
        var graph = GraphDsl.Create(
            Sink.Seq<HttpResponseMessage>(),
            (builder, sink) =>
            {
                var b = builder.Add(bidi);
                var source = builder.Add(Source.From(responses));
                var emptyRequestSource = builder.Add(Source.Empty<HttpRequestMessage>());
                var ignoredRequestSink = builder.Add(Sink.Ignore<HttpRequestMessage>());

                builder.From(emptyRequestSource).To(b.Inlet1);
                builder.From(b.Outlet1).To(ignoredRequestSink);
                builder.From(source).To(b.Inlet2);
                builder.From(b.Outlet2).To(sink);

                return ClosedShape.Instance;
            });

        return RunnableGraph.FromGraph(graph).Run(Materializer);
    }

    private static HttpResponseMessage MakeResponse(HttpRequestMessage? originalRequest = null)
    {
        var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK);
        if (originalRequest is not null)
        {
            response.RequestMessage = originalRequest;
        }

        return response;
    }

    [Fact(Timeout = 10_000)]
    public async Task HandlerBidiStage_should_inject_header_when_sync_request_transformation_applied()
    {
        var handler = new RequestHeaderHandler("X-Trace", "abc");
        var stage = new HandlerBidiStage(handler, 0);
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");

        var results = await RunRequestAsync(stage, request);

        var result = Assert.Single(results);
        Assert.True(result.Headers.Contains("X-Trace"));
        Assert.Equal("abc", result.Headers.GetValues("X-Trace").Single());
    }

    [Fact(Timeout = 10_000)]
    public async Task HandlerBidiStage_should_inject_header_when_sync_response_transformation_applied()
    {
        var handler = new ResponseHeaderHandler("X-Resp", "injected");
        var stage = new HandlerBidiStage(handler, 0);
        var originalRequest = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        var response = MakeResponse(originalRequest);

        var results = await RunResponseAsync(stage, response);

        var result = Assert.Single(results);
        Assert.True(result.Headers.Contains("X-Resp"));
        Assert.Equal("injected", result.Headers.GetValues("X-Resp").Single());
    }

    [Fact(Timeout = 10_000)]
    public async Task HandlerBidiStage_should_receive_original_request_in_response_direction()
    {
        var capturedOriginal = new TaskCompletionSource<HttpRequestMessage>();

        var handler = new CapturingResponseHandler(capturedOriginal);
        var stage = new HandlerBidiStage(handler, 0);
        var originalRequest = new HttpRequestMessage(HttpMethod.Post, "http://example.com/api");
        originalRequest.Headers.TryAddWithoutValidation("X-OriginalMarker", "present");
        var response = MakeResponse(originalRequest);

        await RunResponseAsync(stage, response);

        var captured = await capturedOriginal.Task;
        Assert.Equal(HttpMethod.Post, captured.Method);
        Assert.Equal("http://example.com/api", captured.RequestUri!.ToString());
        Assert.True(captured.Headers.Contains("X-OriginalMarker"));
    }

    private sealed class CapturingResponseHandler : TurboHandler
    {
        private readonly TaskCompletionSource<HttpRequestMessage> _tcs;

        public CapturingResponseHandler(TaskCompletionSource<HttpRequestMessage> tcs) => _tcs = tcs;

        public override HttpResponseMessage ProcessResponse(HttpRequestMessage original, HttpResponseMessage response)
        {
            _tcs.TrySetResult(original);
            return response;
        }
    }

    [Fact(Timeout = 10_000)]
    public async Task HandlerBidiStage_should_apply_cumulative_request_headers_when_composed_atop()
    {
        var h1 = new RequestHeaderHandler("X-First", "1");
        var h2 = new RequestHeaderHandler("X-Second", "2");

        var bidi = BidiFlow.FromGraph(new HandlerBidiStage(h1, 0))
            .Atop(BidiFlow.FromGraph(new HandlerBidiStage(h2, 1)));

        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");

        var results = await RunRequestThroughComposedAsync(bidi, request);

        var result = Assert.Single(results);
        Assert.Equal("1", result.Headers.GetValues("X-First").Single());
        Assert.Equal("2", result.Headers.GetValues("X-Second").Single());
    }

    [Fact(Timeout = 10_000)]
    public async Task HandlerBidiStage_should_apply_cumulative_response_headers_when_composed_atop()
    {
        var h1 = new ResponseHeaderHandler("X-RFirst", "r1");
        var h2 = new ResponseHeaderHandler("X-RSecond", "r2");

        var bidi = BidiFlow.FromGraph(new HandlerBidiStage(h1, 0))
            .Atop(BidiFlow.FromGraph(new HandlerBidiStage(h2, 1)));

        var originalRequest = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        var response = MakeResponse(originalRequest);

        var results = await RunResponseThroughComposedAsync(bidi, response);

        var result = Assert.Single(results);
        Assert.Equal("r1", result.Headers.GetValues("X-RFirst").Single());
        Assert.Equal("r2", result.Headers.GetValues("X-RSecond").Single());
    }

    [Fact(Timeout = 10_000)]
    public async Task HandlerBidiStage_should_flow_through_with_completion_when_multiple_requests_sent()
    {
        var handler = new RequestHeaderHandler("X-Count", "yes");
        var stage = new HandlerBidiStage(handler, 0);

        var requests = Enumerable.Range(1, 5)
            .Select(i => new HttpRequestMessage(HttpMethod.Get, $"http://example.com/{i}"))
            .ToArray();

        var results = await RunRequestAsync(stage, requests);

        Assert.Equal(5, results.Count);
        for (var i = 0; i < results.Count; i++)
        {
            Assert.True(results[i].Headers.Contains("X-Count"));
            Assert.Equal($"http://example.com/{i + 1}", results[i].RequestUri!.ToString());
        }
    }

    [Fact(Timeout = 10_000)]
    public async Task HandlerBidiStage_should_flow_through_with_completion_when_multiple_responses_sent()
    {
        var handler = new ResponseHeaderHandler("X-Processed", "true");
        var stage = new HandlerBidiStage(handler, 0);

        var responses = Enumerable.Range(1, 5)
            .Select(i =>
            {
                var req = new HttpRequestMessage(HttpMethod.Get, $"http://example.com/{i}");
                return MakeResponse(req);
            })
            .ToArray();

        var results = await RunResponseAsync(stage, responses);

        Assert.Equal(5, results.Count);
        foreach (var result in results)
        {
            Assert.True(result.Headers.Contains("X-Processed"));
            Assert.Equal("true", result.Headers.GetValues("X-Processed").Single());
        }
    }
}