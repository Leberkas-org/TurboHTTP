using BenchmarkDotNet.Attributes;
using Servus.Akka.Transport;
using TurboHTTP.MicroBenchmarks.Internal;
using TurboHTTP.Protocol.Http2;

namespace TurboHTTP.MicroBenchmarks.Http2;

[Config(typeof(MicroBenchmarkConfig))]
public class Http2FrameDecoderBenchmark
{
    private byte[] _settingsFrame = null!;
    private byte[] _dataFrame = null!;
    private byte[] _multipleFrames = null!;
    private FrameDecoder _decoder = null!;

    [GlobalSetup]
    public void Setup()
    {
        _decoder = new FrameDecoder();

        _settingsFrame =
        [
            0x00, 0x00, 0x06,
            0x04,
            0x00,
            0x00, 0x00, 0x00, 0x00,
            0x00, 0x04,
            0x00, 0x00, 0xFF, 0xFF
        ];

        var payload = new byte[128];
        Array.Fill(payload, (byte)'D');
        _dataFrame = new byte[9 + payload.Length];
        _dataFrame[0] = 0x00;
        _dataFrame[1] = 0x00;
        _dataFrame[2] = (byte)payload.Length;
        _dataFrame[3] = 0x00;
        _dataFrame[4] = 0x01;
        _dataFrame[5] = 0x00;
        _dataFrame[6] = 0x00;
        _dataFrame[7] = 0x00;
        _dataFrame[8] = 0x01;
        Array.Copy(payload, 0, _dataFrame, 9, payload.Length);

        var ms = new MemoryStream();
        for (var i = 0; i < 10; i++)
        {
            ms.Write(new byte[] { 0x00, 0x00, 0x00, 0x04, 0x01, 0x00, 0x00, 0x00, 0x00 });
        }
        _multipleFrames = ms.ToArray();
    }

    [GlobalCleanup]
    public void Cleanup() => _decoder.Dispose();

    [Benchmark(Baseline = true)]
    public int DecodeSettingsFrame()
    {
        _decoder.Reset();
        TransportBuffer buf = _settingsFrame;
        var frames = _decoder.Decode(buf);
        return frames.Count;
    }

    [Benchmark]
    public int DecodeDataFrame()
    {
        _decoder.Reset();
        TransportBuffer buf = _dataFrame;
        var frames = _decoder.Decode(buf);
        return frames.Count;
    }

    [Benchmark]
    public int Decode10SettingsAck()
    {
        _decoder.Reset();
        TransportBuffer buf = _multipleFrames;
        var frames = _decoder.Decode(buf);
        return frames.Count;
    }
}
