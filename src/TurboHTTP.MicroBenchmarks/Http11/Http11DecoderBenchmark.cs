using BenchmarkDotNet.Attributes;
using TurboHTTP.MicroBenchmarks.Internal;
using TurboHTTP.Protocol.Http11;

namespace TurboHTTP.MicroBenchmarks.Http11;

[Config(typeof(MicroBenchmarkConfig))]
public class Http11DecoderBenchmark
{
    private byte[] _smallResponse = null!;
    private byte[] _largeResponse = null!;
    private byte[] _multipleHeaders = null!;
    private Decoder _decoder = null!;

    [GlobalSetup]
    public void Setup()
    {
        _decoder = new Decoder();

        _smallResponse = "HTTP/1.1 200 OK\r\nContent-Length: 5\r\nConnection: keep-alive\r\n\r\nHello"u8.ToArray();

        _largeResponse = System.Text.Encoding.Latin1.GetBytes(
            string.Concat("HTTP/1.1 200 OK\r\nContent-Length: 8192\r\nConnection: keep-alive\r\n\r\n",
                new string('X', 8192)));

        var headers = new System.Text.StringBuilder();
        headers.Append("HTTP/1.1 200 OK\r\n");
        for (var i = 0; i < 50; i++)
        {
            headers.Append($"X-Header-{i}: value-{i}\r\n");
        }
        headers.Append("Content-Length: 2\r\n\r\nOK");
        _multipleHeaders = System.Text.Encoding.Latin1.GetBytes(headers.ToString());
    }

    [GlobalCleanup]
    public void Cleanup() => _decoder.Dispose();

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

    [Benchmark]
    public bool Decode50Headers()
    {
        _decoder.Reset();
        return _decoder.TryDecode(_multipleHeaders, out _);
    }
}
