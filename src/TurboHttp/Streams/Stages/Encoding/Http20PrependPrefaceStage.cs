using System;
using System.Buffers;
using System.Buffers.Binary;
using Akka.Event;
using Akka.Streams;
using Akka.Streams.Stage;
using TurboHttp.Internal;
using TurboHttp.Protocol.RFC9113;

namespace TurboHttp.Streams.Stages.Encoding;

// RFC 9113 §3.4 — prepend connection preface to the first outbound bytes.
// Each GroupByHostKey substream has its own PrependPrefaceStage instance,
// so a simple boolean suffices — no per-host tracking needed.
public sealed class Http20PrependPrefaceStage : GraphStage<FlowShape<IOutputItem, IOutputItem>>
{
    private readonly Inlet<IOutputItem> _in = new("PrependPreface.In");
    private readonly Outlet<IOutputItem> _out = new("PrependPreface.Out");

    private readonly int _initialWindowSize;

    public Http20PrependPrefaceStage(int initialWindowSize = 65535)
    {
        _initialWindowSize = initialWindowSize;
    }

    public override FlowShape<IOutputItem, IOutputItem> Shape
        => new(_in, _out);

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        => new Logic(this);

    private sealed class Logic : GraphStageLogic
    {
        private readonly Http20PrependPrefaceStage _stage;
        private bool _prefaceSent;

        public Logic(Http20PrependPrefaceStage stage) : base(stage.Shape)
        {
            _stage = stage;

            SetHandler(stage._out, onPull: () =>
            {
                if (!_prefaceSent)
                {
                    _prefaceSent = true;
                    var preface = BuildHttp2ConnectionPreface();
                    var owner = MemoryPool<byte>.Shared.Rent(preface.Length);
                    ((ReadOnlySpan<byte>)preface).CopyTo(owner.Memory.Span);
                    Push(stage._out, new DataItem(owner, preface.Length));
                    return;
                }

                Pull(stage._in);
            });

            SetHandler(stage._in,
                onPush: () => Push(stage._out, Grab(stage._in)),
                onUpstreamFinish: CompleteStage,
                onUpstreamFailure: ex => Log.Warning("PrependPrefaceStage: Upstream failure absorbed: {0}", ex.Message));
        }

        // RFC 9113 §3.4 — Build HTTP/2 connection preface with default SETTINGS
        private byte[] BuildHttp2ConnectionPreface()
        {
            const int frameHeaderSize = 9;
            var windowSize = _stage._initialWindowSize;
            var magic = "PRI * HTTP/2.0\r\n\r\nSM\r\n\r\n"u8.ToArray();

            // Default SETTINGS: HeaderTableSize, EnablePush, InitialWindowSize, MaxFrameSize
            var settingsParams = new (SettingsParameter, uint)[]
            {
                (SettingsParameter.HeaderTableSize, 4096),
                (SettingsParameter.EnablePush, 0),
                (SettingsParameter.InitialWindowSize, (uint)windowSize),
                (SettingsParameter.MaxFrameSize, 16384),
            };

            var settingsPayloadSize = settingsParams.Length * 6;

            // If window size exceeds RFC default, include a WINDOW_UPDATE for connection (stream 0)
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

            // Write SETTINGS frame header (streamId=0, no flags)
            var frameHeaderSpan = result.AsSpan(offset, frameHeaderSize);
            frameHeaderSpan[0] = (byte)(settingsPayloadSize >> 16);
            frameHeaderSpan[1] = (byte)(settingsPayloadSize >> 8);
            frameHeaderSpan[2] = (byte)settingsPayloadSize;
            frameHeaderSpan[3] = (byte)FrameType.Settings;
            frameHeaderSpan[4] = 0; // flags
            BinaryPrimitives.WriteUInt32BigEndian(frameHeaderSpan[5..], 0); // streamId=0
            offset += frameHeaderSize;

            // Write SETTINGS parameters
            var settingsSpan = result.AsSpan(offset, settingsPayloadSize);
            foreach (var (key, val) in settingsParams)
            {
                BinaryPrimitives.WriteUInt16BigEndian(settingsSpan, (ushort)key);
                BinaryPrimitives.WriteUInt32BigEndian(settingsSpan[2..], val);
                settingsSpan = settingsSpan[6..];
            }

            offset += settingsPayloadSize;

            // Connection-level WINDOW_UPDATE to raise from RFC default 65535
            if (needsWindowUpdate)
            {
                var windowUpdateIncrement = windowSize - 65535;
                var winSpan = result.AsSpan(offset);
                winSpan[0] = 0;
                winSpan[1] = 0;
                winSpan[2] = windowUpdatePayloadSize;
                winSpan[3] = (byte)FrameType.WindowUpdate;
                winSpan[4] = 0; // flags
                BinaryPrimitives.WriteUInt32BigEndian(winSpan[5..], 0); // streamId=0
                BinaryPrimitives.WriteUInt32BigEndian(winSpan[9..], (uint)windowUpdateIncrement);
            }

            return result;
        }
    }
}