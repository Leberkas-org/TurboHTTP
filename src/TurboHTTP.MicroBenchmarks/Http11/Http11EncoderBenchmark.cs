using BenchmarkDotNet.Attributes;
using TurboHTTP.MicroBenchmarks.Internal;
using TurboHTTP.Protocol.Http11;

namespace TurboHTTP.MicroBenchmarks.Http11;

[Config(typeof(MicroBenchmarkConfig))]
public class Http11EncoderBenchmark
{
    private HttpRequestMessage _simpleGet = null!;
    private HttpRequestMessage _postWithBody = null!;
    private byte[] _buffer = null!;

    [GlobalSetup]
    public void Setup()
    {
        _buffer = new byte[16384];

        _simpleGet = new HttpRequestMessage(HttpMethod.Get, "http://example.com/path")
        {
            Version = new Version(1, 1)
        };

        _postWithBody = new HttpRequestMessage(HttpMethod.Post, "http://example.com/api/data")
        {
            Version = new Version(1, 1)
        };
        _postWithBody.Headers.TryAddWithoutValidation("Accept", "application/json");
        _postWithBody.Headers.TryAddWithoutValidation("Authorization", "Bearer token123456789");
        _postWithBody.Headers.TryAddWithoutValidation("X-Request-Id", "perf-bench-001");
        _postWithBody.Content = new ByteArrayContent(new byte[1024]);
        _postWithBody.Content.Headers.TryAddWithoutValidation("Content-Type", "application/octet-stream");
    }

    [Benchmark(Baseline = true)]
    public int EncodeSimpleGet()
    {
        var span = _buffer.AsSpan();
        return Encoder.Encode(_simpleGet, ref span);
    }

    [Benchmark]
    public int EncodePostWithBody()
    {
        var span = _buffer.AsSpan();
        return Encoder.Encode(_postWithBody, ref span);
    }
}
