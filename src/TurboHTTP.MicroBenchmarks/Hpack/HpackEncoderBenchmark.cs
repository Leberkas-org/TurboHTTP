using BenchmarkDotNet.Attributes;
using TurboHTTP.MicroBenchmarks.Internal;
using TurboHTTP.Protocol.Http2.Hpack;

namespace TurboHTTP.MicroBenchmarks.Hpack;

[Config(typeof(MicroBenchmarkConfig))]
public class HpackEncoderBenchmark
{
    private HpackEncoder _encoder = null!;
    private List<HpackHeader> _typicalHeaders = null!;
    private List<HpackHeader> _largeHeaderSet = null!;
    private byte[] _outputBuffer = null!;

    [GlobalSetup]
    public void Setup()
    {
        _encoder = new HpackEncoder(useHuffman: true);
        _outputBuffer = new byte[65536];

        _typicalHeaders =
        [
            new(":method", "GET"),
            new(":scheme", "https"),
            new(":path", "/api/v1/users"),
            new(":authority", "example.com"),
            new("accept", "application/json"),
            new("accept-encoding", "gzip, deflate, br"),
            new("user-agent", "TurboHTTP/1.0"),
        ];

        _largeHeaderSet = [];
        _largeHeaderSet.Add(new(":method", "POST"));
        _largeHeaderSet.Add(new(":scheme", "https"));
        _largeHeaderSet.Add(new(":path", "/api/v1/data"));
        _largeHeaderSet.Add(new(":authority", "example.com"));
        for (var i = 0; i < 30; i++)
        {
            _largeHeaderSet.Add(new($"x-custom-header-{i}", $"value-{i}-with-some-content"));
        }
    }

    [Benchmark(Baseline = true)]
    public int EncodeTypicalHeaders()
    {
        var span = _outputBuffer.AsSpan();
        return _encoder.Encode(_typicalHeaders, ref span);
    }

    [Benchmark]
    public int Encode34Headers()
    {
        var span = _outputBuffer.AsSpan();
        return _encoder.Encode(_largeHeaderSet, ref span);
    }
}
