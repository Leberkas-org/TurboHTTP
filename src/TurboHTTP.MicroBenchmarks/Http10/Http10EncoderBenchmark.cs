using BenchmarkDotNet.Attributes;
using TurboHTTP.MicroBenchmarks.Internal;
using TurboHTTP.Protocol.Http10;

namespace TurboHTTP.MicroBenchmarks.Http10;

[Config(typeof(MicroBenchmarkConfig))]
public class Http10EncoderBenchmark
{
    private HttpRequestMessage _simpleGet = null!;
    private HttpRequestMessage _requestWithHeaders = null!;
    private byte[] _buffer = null!;

    [GlobalSetup]
    public void Setup()
    {
        _buffer = new byte[16384];

        _simpleGet = new HttpRequestMessage(HttpMethod.Get, "http://example.com/path");

        _requestWithHeaders = new HttpRequestMessage(HttpMethod.Post, "http://example.com/api/data");
        _requestWithHeaders.Headers.TryAddWithoutValidation("Accept", "application/json");
        _requestWithHeaders.Headers.TryAddWithoutValidation("Authorization", "Bearer token123");
        _requestWithHeaders.Headers.TryAddWithoutValidation("X-Request-Id", "bench-001");
        _requestWithHeaders.Headers.TryAddWithoutValidation("Cache-Control", "no-cache");
        _requestWithHeaders.Content = new ByteArrayContent(new byte[256]);
    }

    [Benchmark(Baseline = true)]
    public int EncodeSimpleGet()
    {
        var span = _buffer.AsSpan();
        return Encoder.Encode(_simpleGet, ref span);
    }

    [Benchmark]
    public int EncodeWithHeaders()
    {
        var span = _buffer.AsSpan();
        return Encoder.Encode(_requestWithHeaders, ref span);
    }
}
