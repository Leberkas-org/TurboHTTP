using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using Akka.Streams;
using Akka.Streams.Stage;
using TurboHttp.IO;
using TurboHttp.Protocol;

namespace TurboHttp.Streams.Stages;

// ── RFC 7540 §3.5 — prepend connection preface to the first outbound bytes ──
public sealed class PrependPrefaceStage : GraphStage<FlowShape<ITransportItem, ITransportItem>>
{
    private readonly Inlet<ITransportItem> _inlet = new("preface.in");
    private readonly Outlet<ITransportItem> _outlet = new("preface.out");

    private readonly int _initialWindowSize;

    public PrependPrefaceStage(int initialWindowSize = 65535)
    {
        _initialWindowSize = initialWindowSize;
    }

    public override FlowShape<ITransportItem, ITransportItem> Shape
        => new(_inlet, _outlet);

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        => new Logic(this);

    private sealed class Logic : GraphStageLogic
    {
        private readonly Dictionary<string, bool> _prefaceSentHost = new();
        private readonly PrependPrefaceStage _stage;

        public Logic(PrependPrefaceStage stage) : base(stage.Shape)
        {
            _stage = stage;

            SetHandler(stage._outlet, onPull: () => Pull(stage._inlet));

            SetHandler(stage._inlet,
                onPush: () =>
                {
                    var item = Grab(stage._inlet);
                    if (item is ConnectItem connectItem)
                    {
                        var prefix = connectItem.Options is TlsOptions ? "TLS" : "TCP";
                        var key = $"{prefix}:{connectItem.Options.Host}:{connectItem.Options.Port}";
                        if (_prefaceSentHost.ContainsKey(key))
                        {
                            return;
                        }

                        var preface = BuildHttp2ConnectionPreface();
                        var owner = MemoryPool<byte>.Shared.Rent(preface.Length);
                        ((ReadOnlySpan<byte>)preface).CopyTo(owner.Memory.Span);
                        EmitMultiple(stage._outlet, [item, new DataItem(owner, preface.Length)]);
                        _prefaceSentHost[key] = true;
                    }
                    else
                    {
                        Push(stage._outlet, item);
                    }
                },
                onUpstreamFinish: CompleteStage,
                onUpstreamFailure: FailStage);
        }

        // ── RFC 7540 §3.5 — Build HTTP/2 connection preface with default SETTINGS ──
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
                winSpan[2] = (byte)windowUpdatePayloadSize;
                winSpan[3] = (byte)FrameType.WindowUpdate;
                winSpan[4] = 0; // flags
                BinaryPrimitives.WriteUInt32BigEndian(winSpan[5..], 0); // streamId=0
                BinaryPrimitives.WriteUInt32BigEndian(winSpan[9..], (uint)windowUpdateIncrement);
            }

            return result;
        }
    }
}
