using TurboHTTP.Client;
using System.Text;
using Akka;
using Akka.Streams.Dsl;
using Servus.Akka.Transport;
using TurboHTTP.Streams;
using Xunit;

namespace TurboHTTP.Tests.Shared;

public abstract class AcceptanceTestBase : EngineTestBase
{
    internal static IHttpProtocolEngine CreateHttp10Engine(Action<Http1Options>? configure = null)
    {
        var clientOptions = new TurboClientOptions();
        configure?.Invoke(clientOptions.Http1);
        return new Http10Engine(clientOptions);
    }

    internal static IHttpProtocolEngine CreateHttp11Engine(Action<Http1Options>? configure = null)
    {
        var clientOptions = new TurboClientOptions();
        configure?.Invoke(clientOptions.Http1);
        return new Http11Engine(clientOptions);
    }

    internal static IHttpProtocolEngine CreateHttp20Engine(Action<Http2Options>? configure = null)
    {
        var clientOptions = new TurboClientOptions();
        configure?.Invoke(clientOptions.Http2);
        return new Http20Engine(clientOptions);
    }

    internal static IHttpProtocolEngine CreateHttp30Engine(Action<Http3Options>? configure = null)
    {
        var clientOptions = new TurboClientOptions();
        configure?.Invoke(clientOptions.Http3);
        return new Http30Engine(clientOptions);
    }

    internal async Task<HttpResponseMessage> SendScriptedAsync(
        IHttpProtocolEngine engine,
        HttpRequestMessage request,
        Func<int, byte[], byte[]?> responseFactory)
    {
        var stage = CreateScriptedConnection(responseFactory);
        var flow = engine.CreateFlow().Join(stage.AsFlow());

        var tcs = new TaskCompletionSource<HttpResponseMessage>();
        _ = Source.Single(request)
            .Via(flow)
            .RunWith(Sink.ForEach<HttpResponseMessage>(res => tcs.TrySetResult(res)), Materializer);

        return await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    }

    internal async Task<(HttpResponseMessage Response, string RawRequest)> SendScriptedWithCaptureAsync(
        IHttpProtocolEngine engine,
        HttpRequestMessage request,
        Func<int, byte[], byte[]?> responseFactory)
    {
        var stage = CreateAccumulatingScriptedConnection(responseFactory);
        var flow = engine.CreateFlow().Join(stage.AsFlow());

        var tcs = new TaskCompletionSource<HttpResponseMessage>();
        _ = Source.Single(request)
            .Via(flow)
            .RunWith(Sink.ForEach<HttpResponseMessage>(res => tcs.TrySetResult(res)), Materializer);

        var response = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        var rawBuilder = new StringBuilder();
        while (stage.TryGetOutbound(out var outbound))
        {
            if (outbound is TransportData { Buffer: var buf })
            {
                rawBuilder.Append(Encoding.Latin1.GetString(buf.Span));
            }
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
