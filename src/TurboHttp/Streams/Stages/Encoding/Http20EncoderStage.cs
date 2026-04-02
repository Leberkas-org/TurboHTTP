using System.Buffers.Binary;
using Akka.Streams;
using Akka.Streams.Stage;
using TurboHttp.Diagnostics;
using TurboHttp.Internal;
using TurboHttp.Protocol.Http2;

namespace TurboHttp.Streams.Stages.Encoding;

/// <summary>
/// Encodes a batch of <see cref="Http2Frame"/> objects into a single <see cref="NetworkBuffer"/>.
/// <para>
/// The upstream <see cref="Http20ConnectionStage"/> emits one frame at a time through
/// <c>OutServer</c>. A <c>BatchWeighted</c> operator sits between the two stages and
/// accumulates frames while the encoder is busy, so each <see cref="OnPush"/> receives
/// as many frames as were ready — reducing scheduling round-trips under load.
/// </para>
/// <para>
/// On the very first downstream pull, the RFC 9113 §3.4 connection preface is emitted
/// before any frame (when <paramref name="initialWindowSize"/> &gt; 0).
/// </para>
/// </summary>
public sealed class Http20EncoderStage : GraphStage<FlowShape<List<Http2Frame>, IOutputItem>>
{
    private readonly Inlet<List<Http2Frame>> _in = new("Http20Encoder.In");
    private readonly Outlet<IOutputItem> _out = new("Http20Encoder.Out");

    // 0 = no preface; > 0 = emit RFC 9113 §3.4 connection preface on first pull.
    private readonly int _initialWindowSize;

    public Http20EncoderStage(int initialWindowSize = 0)
    {
        _initialWindowSize = initialWindowSize;
    }

    public override FlowShape<List<Http2Frame>, IOutputItem> Shape => new(_in, _out);

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        => new Logic(this);

    private sealed class Logic : GraphStageLogic
    {
        private readonly Http20EncoderStage _stage;
        private bool _prefaceSent;
        private RequestEndpoint _endpoint;

        public Logic(Http20EncoderStage stage) : base(stage.Shape)
        {
            _stage = stage;

            SetHandler(stage._in, () =>
            {
                var frames = Grab(stage._in);

                // Capture endpoint from first tagged frame in the batch.
                if (_endpoint == default)
                {
                    foreach (var f in frames)
                    {
                        if (f.Endpoint.HasValue)
                        {
                            _endpoint = f.Endpoint.Value;
                            break;
                        }
                    }
                }

                // Compute total serialized size up-front so we rent exactly one buffer.
                var totalSize = 0;
                foreach (var f in frames)
                {
                    totalSize += f.SerializedSize;
                }

                var buf = NetworkBuffer.Rent(totalSize);
                var span = buf.FullMemory.Span;

                foreach (var f in frames)
                {
                    f.WriteTo(ref span);
                }

                buf.Length = totalSize;
                buf.Key = _endpoint;

                TurboTrace.Protocol.Trace(this, $"Frame batch sent: {frames.Count} frames, {totalSize} bytes");

                Push(stage._out, buf);
            });

            SetHandler(stage._out, () =>
            {
                if (_stage._initialWindowSize > 0 && !_prefaceSent)
                {
                    _prefaceSent = true;
                    var preface = BuildHttp2ConnectionPreface(_stage._initialWindowSize);
                    var prefaceBuf = NetworkBuffer.Rent(preface.Length);
                    ((ReadOnlySpan<byte>)preface).CopyTo(prefaceBuf.FullMemory.Span);
                    prefaceBuf.Length = preface.Length;
                    Push(stage._out, prefaceBuf);
                    return;
                }

                Pull(stage._in);
            });
        }

        // RFC 9113 §3.4 — Build HTTP/2 connection preface with default SETTINGS.
        private static byte[] BuildHttp2ConnectionPreface(int windowSize)
        {
            const int frameHeaderSize = 9;
            var magic = "PRI * HTTP/2.0\r\n\r\nSM\r\n\r\n"u8.ToArray();

            var settingsParams = new (SettingsParameter, uint)[]
            {
                (SettingsParameter.HeaderTableSize, 4096),
                (SettingsParameter.EnablePush, 0),
                (SettingsParameter.InitialWindowSize, (uint)windowSize),
                (SettingsParameter.MaxFrameSize, 16384),
            };

            var settingsPayloadSize = settingsParams.Length * 6;
            var needsWindowUpdate = windowSize > 65535;
            const int windowUpdatePayloadSize = 4;
            var totalSize = magic.Length + frameHeaderSize + settingsPayloadSize;
            if (needsWindowUpdate)
            {
                totalSize += frameHeaderSize + windowUpdatePayloadSize;
            }

            var result = new byte[totalSize];
            magic.CopyTo(result, 0);
            var offset = magic.Length;

            var frameHeaderSpan = result.AsSpan(offset, frameHeaderSize);
            frameHeaderSpan[0] = (byte)(settingsPayloadSize >> 16);
            frameHeaderSpan[1] = (byte)(settingsPayloadSize >> 8);
            frameHeaderSpan[2] = (byte)settingsPayloadSize;
            frameHeaderSpan[3] = (byte)FrameType.Settings;
            frameHeaderSpan[4] = 0;
            BinaryPrimitives.WriteUInt32BigEndian(frameHeaderSpan[5..], 0);
            offset += frameHeaderSize;

            var settingsSpan = result.AsSpan(offset, settingsPayloadSize);
            foreach (var (key, val) in settingsParams)
            {
                BinaryPrimitives.WriteUInt16BigEndian(settingsSpan, (ushort)key);
                BinaryPrimitives.WriteUInt32BigEndian(settingsSpan[2..], val);
                settingsSpan = settingsSpan[6..];
            }

            offset += settingsPayloadSize;

            if (needsWindowUpdate)
            {
                var windowUpdateIncrement = windowSize - 65535;
                var winSpan = result.AsSpan(offset);
                winSpan[0] = 0;
                winSpan[1] = 0;
                winSpan[2] = windowUpdatePayloadSize;
                winSpan[3] = (byte)FrameType.WindowUpdate;
                winSpan[4] = 0;
                BinaryPrimitives.WriteUInt32BigEndian(winSpan[5..], 0);
                BinaryPrimitives.WriteUInt32BigEndian(winSpan[9..], (uint)windowUpdateIncrement);
            }

            return result;
        }
    }
}
