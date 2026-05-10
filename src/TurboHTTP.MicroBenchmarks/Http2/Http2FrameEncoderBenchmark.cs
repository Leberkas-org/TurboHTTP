using BenchmarkDotNet.Attributes;
using TurboHTTP.MicroBenchmarks.Internal;
using TurboHTTP.Protocol.Http2;

namespace TurboHTTP.MicroBenchmarks.Http2;

[Config(typeof(MicroBenchmarkConfig))]
public class Http2FrameEncoderBenchmark
{
    private RequestEncoder _encoder = null!;
    private HttpRequestMessage _simpleGet = null!;
    private HttpRequestMessage _postWithBody = null!;
    private int _streamId;

    [GlobalSetup]
    public void Setup()
    {
        _encoder = new RequestEncoder(useHuffman: true);

        _simpleGet = new HttpRequestMessage(HttpMethod.Get, "https://example.com/path")
        {
            Version = new Version(2, 0)
        };

        _postWithBody = new HttpRequestMessage(HttpMethod.Post, "https://example.com/api/data")
        {
            Version = new Version(2, 0)
        };
        _postWithBody.Headers.TryAddWithoutValidation("Accept", "application/json");
        _postWithBody.Headers.TryAddWithoutValidation("Authorization", "Bearer token123456789");
        _postWithBody.Content = new ByteArrayContent(new byte[1024]);
    }

    [Benchmark(Baseline = true)]
    public int EncodeSimpleGet()
    {
        _streamId += 2;
        var frames = _encoder.Encode(_simpleGet, _streamId);
        return frames.Count;
    }

    [Benchmark]
    public int EncodePostWithBody()
    {
        _streamId += 2;
        var frames = _encoder.Encode(_postWithBody, _streamId);
        return frames.Count;
    }
}
