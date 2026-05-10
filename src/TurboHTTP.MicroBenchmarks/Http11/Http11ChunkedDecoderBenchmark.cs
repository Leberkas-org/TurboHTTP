using BenchmarkDotNet.Attributes;
using TurboHTTP.MicroBenchmarks.Internal;
using TurboHTTP.Protocol.Http11;

namespace TurboHTTP.MicroBenchmarks.Http11;

[Config(typeof(MicroBenchmarkConfig))]
public class Http11ChunkedDecoderBenchmark
{
    private byte[] _singleChunk = null!;
    private byte[] _manySmallChunks = null!;
    private Decoder _decoder = null!;

    [GlobalSetup]
    public void Setup()
    {
        _decoder = new Decoder();

        _singleChunk = System.Text.Encoding.Latin1.GetBytes(
            "HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\n\r\n" +
            "100\r\n" + new string('A', 256) + "\r\n0\r\n\r\n");

        var sb = new System.Text.StringBuilder();
        sb.Append("HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\n\r\n");
        for (var i = 0; i < 20; i++)
        {
            sb.Append("10\r\n");
            sb.Append(new string('B', 16));
            sb.Append("\r\n");
        }
        sb.Append("0\r\n\r\n");
        _manySmallChunks = System.Text.Encoding.Latin1.GetBytes(sb.ToString());
    }

    [GlobalCleanup]
    public void Cleanup() => _decoder.Dispose();

    [Benchmark(Baseline = true)]
    public bool DecodeSingleChunk()
    {
        _decoder.Reset();
        return _decoder.TryDecode(_singleChunk, out _);
    }

    [Benchmark]
    public bool Decode20SmallChunks()
    {
        _decoder.Reset();
        return _decoder.TryDecode(_manySmallChunks, out _);
    }
}
