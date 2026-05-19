using TurboHTTP.Client;
using System.IO.Compression;
using System.Net;
using Akka.Streams.Dsl;
using TurboHTTP.Features.Cookies;
using TurboHTTP.Protocol.Semantics;
using TurboHTTP.Streams.Stages.Features;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.AcceptanceTests.H3;

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

    private sealed class ResponseTransformHandler : TurboHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage, HttpResponseMessage> _transform;

        public ResponseTransformHandler(Func<HttpRequestMessage, HttpResponseMessage, HttpResponseMessage> transform)
        {
            _transform = transform;
        }

        public override HttpResponseMessage ProcessResponse(HttpRequestMessage original, HttpResponseMessage response)
            => _transform(original, response);
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

    private static byte[] GzipCompress(byte[] data)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionMode.Compress))
        {
            gzip.Write(data, 0, data.Length);
        }

        return output.ToArray();
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

    private async Task<HttpResponseMessage> SendWithHandlerRedirectAsync(ResponseMap map,
        HttpRequestMessage request, TurboHandler handler)
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

    private async Task<HttpResponseMessage> SendWithHandlerCompressionAsync(ResponseMap map,
        HttpRequestMessage request, TurboHandler handler)
    {
        var handlerStage = BidiFlow.FromGraph(new HandlerBidiStage(handler, 0));
        var contentEncoding = BidiFlow.FromGraph(new ContentEncodingBidiStage());
        var fake = ResponseMapFake.Create(map);
        var flow = handlerStage.Atop(contentEncoding).Atop(fake)
            .Join(Flow.FromFunction<HttpRequestMessage, HttpResponseMessage>(_ => new HttpResponseMessage()));

        var tcs = new TaskCompletionSource<HttpResponseMessage>();
        _ = Source.Single(request)
            .Via(flow)
            .RunWith(Sink.ForEach<HttpResponseMessage>(res => tcs.TrySetResult(res)), Materializer);

        return await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    }

    private async Task<HttpResponseMessage> SendWithHandlerCookieAsync(ResponseMap map,
        HttpRequestMessage request, TurboHandler handler, CookieJar jar)
    {
        var handlerStage = BidiFlow.FromGraph(new HandlerBidiStage(handler, 0));
        var cookie = BidiFlow.FromGraph(new CookieBidiStage(jar));
        var fake = ResponseMapFake.Create(map);
        var flow = handlerStage.Atop(cookie).Atop(fake)
            .Join(Flow.FromFunction<HttpRequestMessage, HttpResponseMessage>(_ => new HttpResponseMessage()));

        var tcs = new TaskCompletionSource<HttpResponseMessage>();
        _ = Source.Single(request)
            .Via(flow)
            .RunWith(Sink.ForEach<HttpResponseMessage>(res => tcs.TrySetResult(res)), Materializer);

        return await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-9.3.1")]
    public async Task HandlerPipeline_should_inject_custom_header_with_use_request()
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
    public async Task HandlerPipeline_should_add_header_to_response_with_use_response()
    {
        var map = new ResponseMap()
            .On("/hello", _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("Hello World")
            });

        var handler = new ResponseTransformHandler((_, res) =>
        {
            res.Headers.TryAddWithoutValidation("X-Handler-Added", "injected");
            return res;
        });

        var response = await SendWithHandlerAsync(map,
            new HttpRequestMessage(HttpMethod.Get, "http://localhost/hello"), handler);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(
            response.Headers.TryGetValues("X-Handler-Added", out var vals),
            "X-Handler-Added header must be present on the response");
        Assert.Equal("injected", string.Join(",", vals));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-9.3.1")]
    public async Task HandlerPipeline_should_process_request_with_typed_handler()
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
    public async Task HandlerPipeline_should_execute_multiple_handlers_in_registration_order()
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
    [Trait("RFC", "RFC9110-9.3.1")]
    public async Task HandlerPipeline_should_see_original_request_on_response()
    {
        string? capturedOriginalUrl = null;

        var map = new ResponseMap()
            .On("/hello", req =>
            {
                var r = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("Hello World")
                };
                r.RequestMessage = req;
                return r;
            });

        var handler = new ResponseTransformHandler((original, res) =>
        {
            capturedOriginalUrl = original.RequestUri?.PathAndQuery;
            return res;
        });

        var response = await SendWithHandlerAsync(map,
            new HttpRequestMessage(HttpMethod.Get, "http://localhost/hello"), handler);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("/hello", capturedOriginalUrl);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-15.4")]
    public async Task HandlerPipeline_should_work_with_redirect_pipeline()
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

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-9.3.1")]
    public async Task HandlerPipeline_should_work_with_compression_pipeline()
    {
        var payload = new byte[1024];
        for (var i = 0; i < payload.Length; i++)
        {
            payload[i] = (byte)('A' + i % 26);
        }

        var compressed = GzipCompress(payload);

        var map = new ResponseMap()
            .On("/compress/gzip/1", _ =>
            {
                var r = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(compressed)
                };
                r.Content.Headers.TryAddWithoutValidation("Content-Encoding", "gzip");
                return r;
            });

        var handler = new ResponseTransformHandler((_, res) => res);

        var response = await SendWithHandlerCompressionAsync(map,
            new HttpRequestMessage(HttpMethod.Get, "http://localhost/compress/gzip/1"), handler);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal(1024, body.Length);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC6265-5.4")]
    public async Task HandlerPipeline_should_work_with_cookie_pipeline()
    {
        var jar = new CookieJar();

        var map = new ResponseMap()
            .On("/cookie/set/testcookie/testvalue", _ =>
            {
                var r = new HttpResponseMessage(HttpStatusCode.OK);
                r.Headers.TryAddWithoutValidation("Set-Cookie", "testcookie=testvalue; Path=/");
                return r;
            })
            .On("/interaction/echo-all-headers", req =>
            {
                var r = new HttpResponseMessage(HttpStatusCode.OK);
                foreach (var header in req.Headers)
                {
                    if (header.Key.StartsWith("X-", StringComparison.OrdinalIgnoreCase))
                    {
                        r.Headers.TryAddWithoutValidation(header.Key, header.Value);
                    }
                }

                if (req.Headers.TryGetValues("Cookie", out var cookieVals))
                {
                    r.Headers.TryAddWithoutValidation("X-Received-Cookie", string.Join("; ", cookieVals));
                }

                return r;
            });

        var handler = new RequestTransformHandler(req =>
        {
            req.Headers.TryAddWithoutValidation("X-From-Handler", "yes");
            return req;
        });

        await SendWithHandlerCookieAsync(map,
            new HttpRequestMessage(HttpMethod.Get, "http://localhost/cookie/set/testcookie/testvalue"),
            handler, jar);

        var response = await SendWithHandlerCookieAsync(map,
            new HttpRequestMessage(HttpMethod.Get, "http://localhost/interaction/echo-all-headers"),
            handler, jar);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(
            response.Headers.TryGetValues("X-From-Handler", out var handlerVals),
            "X-From-Handler must be echoed back");
        Assert.Equal("yes", string.Join(",", handlerVals));
        Assert.True(
            response.Headers.TryGetValues("X-Received-Cookie", out var cookieHeaders),
            "X-Received-Cookie must be present (cookie jar injected cookie)");
        Assert.Contains("testcookie=testvalue", string.Join(",", cookieHeaders));
    }
}
