using BenchmarkDotNet.Attributes;
using TurboHTTP.MicroBenchmarks.Internal;
using TurboHTTP.Protocol.Http10;

namespace TurboHTTP.MicroBenchmarks.Http10;

[Config(typeof(MicroBenchmarkConfig))]
public class Http10DecoderBenchmark
{
    private byte[] _smallResponse = null!;
    private byte[] _largeResponse = null!;
    private Decoder _decoder = null!;

    [GlobalSetup]
    public void Setup()
    {
        _decoder = new Decoder();

        _smallResponse = "HTTP/1.0 200 OK\r\nContent-Length: 5\r\n\r\nHello"u8.ToArray();

        _largeResponse = System.Text.Encoding.Latin1.GetBytes(
            string.Concat("HTTP/1.0 200 OK\r\nContent-Length: 8192\r\n\r\n",
                new string('X', 8192)));
    }

    [Benchmark(Baseline = true)]
    public bool DecodeSmallResponse()
    {
        _decoder.Reset();
        return _decoder.TryDecode(_smallResponse, out _);
    }

    [Benchmark]
    public bool DecodeLargeResponse()
    {
        _decoder.Reset();
        return _decoder.TryDecode(_largeResponse, out _);
    }
}
