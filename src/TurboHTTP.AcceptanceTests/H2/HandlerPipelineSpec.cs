using TurboHTTP.Client;
using System.Net;
using Akka.Streams.Dsl;
using TurboHTTP.Protocol.Semantics;
using TurboHTTP.Streams.Stages.Features;
using TurboHTTP.Streams.Stages.Client;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.AcceptanceTests.H2;

public sealed class HandlerPipelineSpec : AcceptanceTestBase
{
    private sealed class TestHeaderHandler : TurboHandler
    {
        public override HttpRequestMessage ProcessRequest(HttpRequestMessage request)
        {
            request.Headers.TryAddWithoutValidation("X-Typed-Handler", "active");
            return request;
        }
    }

    private static HttpResponseMessage EchoHeaders(HttpRequestMessage req)
    {
        var r = new HttpResponseMessage(HttpStatusCode.OK);
        foreach (var header in req.Headers)
        {
            if (header.Key.StartsWith("X-", StringComparison.OrdinalIgnoreCase))
            {
                r.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        return r;
    }

    private async Task<HttpResponseMessage> SendWithHandlerAsync(ResponseMap map, HttpRequestMessage request,
        params TurboHandler[] handlers)
    {
        var fake = ResponseMapFake.Create(map);

        var pipeline = BidiFlow.FromGraph(new HandlerBidiStage(handlers[0], 0));
        for (var i = 1; i < handlers.Length; i++)
        {
            pipeline = pipeline.Atop(BidiFlow.FromGraph(new HandlerBidiStage(handlers[i], i)));
        }

        var flow = pipeline.Atop(fake)
            .Join(Flow.FromFunction<HttpRequestMessage, HttpResponseMessage>(_ => new HttpResponseMessage()));

        var tcs = new TaskCompletionSource<HttpResponseMessage>();
        _ = Source.Single(request)
            .Via(flow)
            .RunWith(Sink.ForEach<HttpResponseMessage>(res => tcs.TrySetResult(res)), Materializer);

        return await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    }

    private async Task<HttpResponseMessage> SendWithHandlerRedirectAsync(ResponseMap map, HttpRequestMessage request,
        TurboHandler handler)
    {
        var handlerStage = BidiFlow.FromGraph(new HandlerBidiStage(handler, 0));
        var redirect = BidiFlow.FromGraph(new RedirectBidiStage(new RedirectPolicy()));
        var fake = ResponseMapFake.Create(map);
        var flow = handlerStage.Atop(redirect).Atop(fake)
            .Join(Flow.FromFunction<HttpRequestMessage, HttpResponseMessage>(_ => new HttpResponseMessage()));

        var tcs = new TaskCompletionSource<HttpResponseMessage>();
        _ = Source.Single(request)
            .Via(flow)
            .RunWith(Sink.ForEach<HttpResponseMessage>(res => tcs.TrySetResult(res)), Materializer);

        return await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-9.3.1")]
    public async Task UseRequest_should_inject_custom_header()
    {
        var map = new ResponseMap()
            .On("/headers/echo", EchoHeaders);

        var handler = new RequestTransformHandler(req =>
        {
            req.Headers.TryAddWithoutValidation("X-Custom-Injected", "hello");
            return req;
        });

        var response = await SendWithHandlerAsync(map,
            new HttpRequestMessage(HttpMethod.Get, "http://localhost/headers/echo"), handler);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(
            response.Headers.TryGetValues("X-Custom-Injected", out var vals),
            "X-Custom-Injected header must be echoed back");
        Assert.Equal("hello", string.Join(",", vals));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-9.3.1")]
    public async Task AddHandler_should_process_request_with_typed_handler()
    {
        var map = new ResponseMap()
            .On("/headers/echo", EchoHeaders);

        var response = await SendWithHandlerAsync(map,
            new HttpRequestMessage(HttpMethod.Get, "http://localhost/headers/echo"),
            new TestHeaderHandler());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(
            response.Headers.TryGetValues("X-Typed-Handler", out var vals),
            "X-Typed-Handler header must be echoed back");
        Assert.Equal("active", string.Join(",", vals));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-9.3.1")]
    public async Task Multiple_handlers_should_execute_in_registration_order()
    {
        var map = new ResponseMap()
            .On("/headers/echo", EchoHeaders);

        var handler1 = new RequestTransformHandler(req =>
        {
            req.Headers.TryAddWithoutValidation("X-First", "1");
            return req;
        });
        var handler2 = new RequestTransformHandler(req =>
        {
            req.Headers.TryAddWithoutValidation("X-Second", "2");
            return req;
        });

        var response = await SendWithHandlerAsync(map,
            new HttpRequestMessage(HttpMethod.Get, "http://localhost/headers/echo"),
            handler1, handler2);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.TryGetValues("X-First", out var firstVals), "X-First must be present");
        Assert.True(response.Headers.TryGetValues("X-Second", out var secondVals), "X-Second must be present");
        Assert.Equal("1", string.Join(",", firstVals));
        Assert.Equal("2", string.Join(",", secondVals));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-15.4")]
    public async Task Handler_should_work_with_redirect_pipeline()
    {
        var map = new ResponseMap()
            .On("/redirect/302/headers/echo", _ =>
            {
                var r = new HttpResponseMessage(HttpStatusCode.Found);
                r.Headers.Location = new Uri("http://localhost/headers/echo");
                return r;
            })
            .On("/headers/echo", EchoHeaders);

        var handler = new RequestTransformHandler(req =>
        {
            req.Headers.TryAddWithoutValidation("X-Handler-Redirect", "present");
            return req;
        });

        var response = await SendWithHandlerRedirectAsync(map,
            new HttpRequestMessage(HttpMethod.Get, "http://localhost/redirect/302/headers/echo"), handler);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(
            response.Headers.TryGetValues("X-Handler-Redirect", out var vals),
            "X-Handler-Redirect must still be present after redirect");
        Assert.Equal("present", string.Join(",", vals));
    }

    private sealed class RequestTransformHandler : TurboHandler
    {
        private readonly Func<HttpRequestMessage, HttpRequestMessage> _transform;

        public RequestTransformHandler(Func<HttpRequestMessage, HttpRequestMessage> transform)
        {
            _transform = transform;
        }

        public override HttpRequestMessage ProcessRequest(HttpRequestMessage request)
            => _transform(request);
    }
}