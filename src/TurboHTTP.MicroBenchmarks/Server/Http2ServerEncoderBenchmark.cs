using BenchmarkDotNet.Attributes;
using Microsoft.AspNetCore.Http.Features;
using TurboHTTP.Context.Features;
using TurboHTTP.MicroBenchmarks.Internal;
using TurboHTTP.Protocol.Syntax.Http2.Server;
using TurboHTTP.Server;

namespace TurboHTTP.MicroBenchmarks.Server;

[Config(typeof(MicroBenchmarkConfig))]
public sealed class Http2ServerEncoderBenchmark
{
    private Http2ServerEncoder _encoder = null!;
    private TurboHttpContext _simpleOkContext = null!;
    private TurboHttpContext _withBodyContext = null!;
    private TurboHttpContext _manyHeadersContext = null!;
    private int _streamId;

    [GlobalSetup]
    public void Setup()
    {
        _encoder = new Http2ServerEncoder();
        _streamId = 1;

        _simpleOkContext = CreateContext(200);

        _withBodyContext = CreateContext(200);
        _withBodyContext.Response.Headers["Content-Type"] = "application/json";
        _withBodyContext.Response.ContentLength = 1024;

        _manyHeadersContext = CreateContext(200);
        for (var i = 0; i < 10; i++)
        {
            _manyHeadersContext.Response.Headers[$"X-Custom-Header-{i}"] = $"value-{i}";
        }
    }

    [Benchmark(Baseline = true)]
    public int EncodeSimpleOk()
    {
        _streamId += 2;
        var frames = _encoder.EncodeHeaders(_simpleOkContext, _streamId, hasBody: false);
        return frames.Count;
    }

    [Benchmark]
    public int EncodeResponseWithBody()
    {
        _streamId += 2;
        var frames = _encoder.EncodeHeaders(_withBodyContext, _streamId, hasBody: true);
        return frames.Count;
    }

    [Benchmark]
    public int EncodeWithManyHeaders()
    {
        _streamId += 2;
        var frames = _encoder.EncodeHeaders(_manyHeadersContext, _streamId, hasBody: false);
        return frames.Count;
    }

    private static TurboHttpContext CreateContext(int statusCode)
    {
        var features = new FeatureCollection();
        features.Set<IHttpRequestFeature>(new TurboHttpRequestFeature());
        var responseFeature = new TurboHttpResponseFeature { StatusCode = statusCode };
        features.Set<IHttpResponseFeature>(responseFeature);
        var bodyFeature = new TurboHttpResponseBodyFeature();
        features.Set<IHttpResponseBodyFeature>(bodyFeature);
        features.Set<IHttpResponseBodyFeature>(bodyFeature);
        return new TurboHttpContext(features);
    }
}
