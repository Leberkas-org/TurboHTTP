using BenchmarkDotNet.Attributes;
using Microsoft.AspNetCore.Http.Features;
using TurboHTTP.Context.Features;
using TurboHTTP.MicroBenchmarks.Internal;
using TurboHTTP.Protocol.Syntax.Http11.Options;
using TurboHTTP.Protocol.Syntax.Http11.Server;
using TurboHTTP.Server;

namespace TurboHTTP.MicroBenchmarks.Server;

[Config(typeof(MicroBenchmarkConfig))]
public sealed class Http11ServerEncoderBenchmark
{
    private byte[] _buffer = null!;
    private Http11ServerEncoder _encoder = null!;
    private TurboHttpContext _simpleOkContext = null!;
    private TurboHttpContext _withBodyContext = null!;
    private TurboHttpContext _manyHeadersContext = null!;

    [GlobalSetup]
    public void Setup()
    {
        _buffer = new byte[16384];
        _encoder = new Http11ServerEncoder(Http11ServerEncoderOptions.Default);

        _simpleOkContext = CreateContext(200, contentLength: 0);

        _withBodyContext = CreateContext(200, contentLength: 1024);
        _withBodyContext.Response.Headers["Content-Type"] = "application/octet-stream";

        _manyHeadersContext = CreateContext(200, contentLength: 0);
        for (var i = 0; i < 10; i++)
        {
            _manyHeadersContext.Response.Headers[$"X-Custom-Header-{i}"] = $"value-{i}";
        }
    }

    [Benchmark(Baseline = true)]
    public int EncodeSimpleOk()
    {
        return _encoder.Encode(_buffer.AsSpan(), _simpleOkContext, isChunked: false, connectionClose: false);
    }

    [Benchmark]
    public int EncodeWithBody()
    {
        return _encoder.Encode(_buffer.AsSpan(), _withBodyContext, isChunked: false, connectionClose: false);
    }

    [Benchmark]
    public int EncodeWithManyHeaders()
    {
        return _encoder.Encode(_buffer.AsSpan(), _manyHeadersContext, isChunked: false, connectionClose: false);
    }

    private static TurboHttpContext CreateContext(int statusCode, long contentLength)
    {
        var features = new FeatureCollection();
        features.Set<IHttpRequestFeature>(new TurboHttpRequestFeature());
        var responseFeature = new TurboHttpResponseFeature { StatusCode = statusCode };
        features.Set<IHttpResponseFeature>(responseFeature);
        var bodyFeature = new TurboHttpResponseBodyFeature();
        features.Set<IHttpResponseBodyFeature>(bodyFeature);
        features.Set<IHttpResponseBodyFeature>(bodyFeature);

        var context = new TurboHttpContext(features);
        context.Response.ContentLength = contentLength;
        return context;
    }
}
