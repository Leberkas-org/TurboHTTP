using System.Text;
using Akka;
using Akka.Streams.Dsl;
using TurboHTTP.Internal;
using TurboHTTP.Streams;
using Xunit;

namespace TurboHTTP.Tests.Shared;

public abstract class AcceptanceTestBase : EngineTestBase
{
    protected static IHttpProtocolEngine CreateHttp10Engine(Action<Http1Options>? configure = null)
    {
        var options = new Http1Options();
        configure?.Invoke(options);
        return new Http10Engine(options.ToEngineOptions());
    }

    protected static IHttpProtocolEngine CreateHttp11Engine(Action<Http1Options>? configure = null)
    {
        var options = new Http1Options();
        configure?.Invoke(options);
        return new Http11Engine(options.ToEngineOptions());
    }

    protected static IHttpProtocolEngine CreateHttp20Engine(Action<Http2Options>? configure = null)
    {
        var options = new Http2Options();
        configure?.Invoke(options);
        return new Http20Engine(options.ToEngineOptions());
    }

    protected static IHttpProtocolEngine CreateHttp30Engine(Action<Http3Options>? configure = null)
    {
        var options = new Http3Options();
        configure?.Invoke(options);
        return new Http30Engine(options.ToEngineOptions());
    }

    protected async Task<HttpResponseMessage> SendScriptedAsync(
        IHttpProtocolEngine engine,
        HttpRequestMessage request,
        Func<int, byte[], byte[]?> responseFactory)
    {
        var fake = new ScriptedFakeConnectionStage(responseFactory);
        var flow = engine.CreateFlow().Join(Flow.FromGraph<IOutputItem, IInputItem, NotUsed>(fake));

        var tcs = new TaskCompletionSource<HttpResponseMessage>();
        _ = Source.Single(request)
            .Via(flow)
            .RunWith(Sink.ForEach<HttpResponseMessage>(res => tcs.TrySetResult(res)), Materializer);

        return await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    }

    protected async Task<(HttpResponseMessage Response, string RawRequest)> SendScriptedWithCaptureAsync(
        IHttpProtocolEngine engine,
        HttpRequestMessage request,
        Func<int, byte[], byte[]?> responseFactory)
    {
        var fake = new ScriptedFakeConnectionStage(responseFactory);
        var flow = engine.CreateFlow().Join(Flow.FromGraph<IOutputItem, IInputItem, NotUsed>(fake));

        var tcs = new TaskCompletionSource<HttpResponseMessage>();
        _ = Source.Single(request)
            .Via(flow)
            .RunWith(Sink.ForEach<HttpResponseMessage>(res => tcs.TrySetResult(res)), Materializer);

        var response = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        var rawBuilder = new StringBuilder();
        while (fake.OutboundChannel.Reader.TryRead(out var chunk))
        {
            rawBuilder.Append(Encoding.Latin1.GetString(chunk.Span));
        }

        return (response, rawBuilder.ToString());
    }

    protected async Task<HttpResponseMessage> SendWithFakeAsync(
        BidiFlow<HttpRequestMessage, HttpRequestMessage, HttpResponseMessage, HttpResponseMessage, NotUsed> featurePipeline,
        ResponseMap map,
        HttpRequestMessage request)
    {
        var fake = ResponseMapFake.Create(map);
        var flow = featurePipeline.Atop(fake)
            .Join(Flow.FromFunction<HttpRequestMessage, HttpResponseMessage>(_ => new HttpResponseMessage()));

        var tcs = new TaskCompletionSource<HttpResponseMessage>();
        _ = Source.Single(request)
            .Via(flow)
            .RunWith(Sink.ForEach<HttpResponseMessage>(res => tcs.TrySetResult(res)), Materializer);

        return await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    }
}
