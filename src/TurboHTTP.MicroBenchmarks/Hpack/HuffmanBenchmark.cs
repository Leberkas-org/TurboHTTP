using BenchmarkDotNet.Attributes;
using TurboHTTP.MicroBenchmarks.Internal;
using TurboHTTP.Protocol;

namespace TurboHTTP.MicroBenchmarks.Hpack;

[Config(typeof(MicroBenchmarkConfig))]
public class HuffmanBenchmark
{
    private byte[] _shortInput = null!;
    private byte[] _longInput = null!;
    private byte[] _shortEncoded = null!;
    private byte[] _longEncoded = null!;
    private byte[] _encodeOutput = null!;
    private byte[] _decodeOutput = null!;

    [GlobalSetup]
    public void Setup()
    {
        _shortInput = "application/json"u8.ToArray();
        _longInput = System.Text.Encoding.ASCII.GetBytes(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");

        _encodeOutput = new byte[HuffmanCodec.GetMaxEncodedLength(_longInput.Length)];
        _decodeOutput = new byte[HuffmanCodec.GetMaxDecodedLength(_longInput.Length)];

        var shortOut = new byte[HuffmanCodec.GetMaxEncodedLength(_shortInput.Length)];
        var shortLen = HuffmanCodec.Encode(_shortInput, shortOut);
        _shortEncoded = shortOut[..shortLen];

        var longOut = new byte[HuffmanCodec.GetMaxEncodedLength(_longInput.Length)];
        var longLen = HuffmanCodec.Encode(_longInput, longOut);
        _longEncoded = longOut[..longLen];
    }

    [Benchmark(Baseline = true)]
    public int EncodeShort()
    {
        return HuffmanCodec.Encode(_shortInput, _encodeOutput);
    }

    [Benchmark]
    public int EncodeLong()
    {
        return HuffmanCodec.Encode(_longInput, _encodeOutput);
    }

    [Benchmark]
    public int DecodeShort()
    {
        return HuffmanCodec.Decode(_shortEncoded, _decodeOutput);
    }

    [Benchmark]
    public int DecodeLong()
    {
        return HuffmanCodec.Decode(_longEncoded, _decodeOutput);
    }
}
