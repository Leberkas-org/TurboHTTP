using System;
using System.Buffers;
using System.Buffers.Binary;
using Akka.Streams;
using Akka.Streams.Stage;
using TurboHttp.Protocol;

namespace TurboHttp.Streams.Stages;

// ── RFC 7540 §3.5 — prepend connection preface to the first outbound bytes ──
public sealed class PrependPrefaceStage : GraphStage<FlowShape<(IMemoryOwner<byte>, int), (IMemoryOwner<byte>, int)>>
{
    private readonly Inlet<(IMemoryOwner<byte>, int)> _inlet = new("preface.in");
    private readonly Outlet<(IMemoryOwner<byte>, int)> _outlet = new("preface.out");

    public override FlowShape<(IMemoryOwner<byte>, int), (IMemoryOwner<byte>, int)> Shape
        => new(_inlet, _outlet);

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        => new Logic(this);

    private sealed class Logic : GraphStageLogic
    {
        private bool _prefaceSent;

        public Logic(PrependPrefaceStage stage) : base(stage.Shape)
        {
            SetHandler(stage._outlet, onPull: () =>
            {
                if (!_prefaceSent)
                {
                    _prefaceSent = true;
                    var preface = BuildHttp2ConnectionPreface();
                    var owner = MemoryPool<byte>.Shared.Rent(preface.Length);
                    ((ReadOnlySpan<byte>)preface).CopyTo(owner.Memory.Span);
                    Push(stage._outlet, (owner, preface.Length));
                }
                else
                {
                    Pull(stage._inlet);
                }
            });

            SetHandler(stage._inlet,
                onPush: () => Push(stage._outlet, Grab(stage._inlet)),
                onUpstreamFinish: CompleteStage,
                onUpstreamFailure: FailStage);
        }

        // ── RFC 7540 §3.5 — Build HTTP/2 connection preface with default SETTINGS ──
        private static byte[] BuildHttp2ConnectionPreface()
        {
            const int frameHeaderSize = 9;
            var magic = "PRI * HTTP/2.0\r\n\r\nSM\r\n\r\n"u8.ToArray();

            // Default SETTINGS: HeaderTableSize, EnablePush, InitialWindowSize, MaxFrameSize
            var settingsParams = new (SettingsParameter, uint)[]
            {
                (SettingsParameter.HeaderTableSize, 4096),
                (SettingsParameter.EnablePush, 0),
                (SettingsParameter.InitialWindowSize, 65535),
                (SettingsParameter.MaxFrameSize, 16384),
            };

            var payloadSize = settingsParams.Length * 6;
            var result = new byte[magic.Length + frameHeaderSize + payloadSize];

            magic.CopyTo(result, 0);

            // Write SETTINGS frame header (streamId=0, no flags)
            var frameHeaderSpan = result.AsSpan(magic.Length, frameHeaderSize);
            frameHeaderSpan[0] = (byte)(payloadSize >> 16);
            frameHeaderSpan[1] = (byte)(payloadSize >> 8);
            frameHeaderSpan[2] = (byte)payloadSize;
            frameHeaderSpan[3] = (byte)FrameType.Settings;
            frameHeaderSpan[4] = 0; // flags
            BinaryPrimitives.WriteUInt32BigEndian(frameHeaderSpan[5..], 0); // streamId=0

            // Write SETTINGS parameters
            var settingsSpan = result.AsSpan(magic.Length + frameHeaderSize);
            foreach (var (key, val) in settingsParams)
            {
                BinaryPrimitives.WriteUInt16BigEndian(settingsSpan, (ushort)key);
                BinaryPrimitives.WriteUInt32BigEndian(settingsSpan[2..], val);
                settingsSpan = settingsSpan[6..];
            }

            return result;
        }
    }
}