using BenchmarkDotNet.Attributes;
using TurboHTTP.MicroBenchmarks.Internal;
using TurboHTTP.Protocol.Http2.Hpack;

namespace TurboHTTP.MicroBenchmarks.Hpack;

[Config(typeof(MicroBenchmarkConfig))]
public class HpackDecoderBenchmark
{
    private HpackDecoder _decoder = null!;
    private byte[] _typicalEncoded = null!;
    private byte[] _largeEncoded = null!;

    [GlobalSetup]
    public void Setup()
    {
        _decoder = new HpackDecoder();

        var encoder = new HpackEncoder(useHuffman: true);

        var typicalHeaders = new List<HpackHeader>
        {
            new(":status", "200"),
            new("content-type", "application/json"),
            new("content-length", "1024"),
            new("server", "nginx"),
            new("date", "Sat, 10 May 2026 12:00:00 GMT"),
            new("cache-control", "max-age=3600"),
            new("vary", "Accept-Encoding"),
        };

        var output = new byte[4096];
        var span = output.AsSpan();
        var written = encoder.Encode(typicalHeaders, ref span);
        _typicalEncoded = output[..written];

        var largeHeaders = new List<HpackHeader>();
        largeHeaders.Add(new(":status", "200"));
        for (var i = 0; i < 30; i++)
        {
            largeHeaders.Add(new($"x-response-header-{i}", $"value-{i}"));
        }

        var largeOutput = new byte[65536];
        var largeSpan = largeOutput.AsSpan();
        var largeEncoder = new HpackEncoder(useHuffman: true);
        var largeWritten = largeEncoder.Encode(largeHeaders, ref largeSpan);
        _largeEncoded = largeOutput[..largeWritten];
    }

    [Benchmark(Baseline = true)]
    public int DecodeTypicalHeaders()
    {
        var headers = _decoder.Decode(_typicalEncoded);
        return headers.Count;
    }

    [Benchmark]
    public int Decode31Headers()
    {
        var headers = _decoder.Decode(_largeEncoded);
        return headers.Count;
    }
}
