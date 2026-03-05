using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using Akka.Streams;
using Akka.Streams.Stage;
using TurboHttp.Protocol;

namespace TurboHttp.Streams.Stages;

public sealed class Http20DecoderStage : GraphStage<FlowShape<(IMemoryOwner<byte>, int), Http2Frame>>
{
    private readonly Inlet<(IMemoryOwner<byte>, int)> _inlet = new("http20.tcp.in");
    private readonly Outlet<Http2Frame> _outlet = new("http20.frame.out");

    public override FlowShape<(IMemoryOwner<byte>, int), Http2Frame> Shape { get; }

    public Http20DecoderStage()
    {
        Shape = new FlowShape<(IMemoryOwner<byte>, int), Http2Frame>(_inlet, _outlet);
    }

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
    {
        return new Logic(this);
    }

    private sealed class Logic : GraphStageLogic
    {
        private readonly MemoryPool<byte> _pool = MemoryPool<byte>.Shared;

        private IMemoryOwner<byte>? _bufferOwner;
        private Memory<byte> _buffer;
        private int _count;

        public Logic(Http20DecoderStage stage) : base(stage.Shape)
        {
            SetHandler(stage._inlet,
                onPush: () =>
                {
                    var (owner, length) = Grab(stage._inlet);

                    try
                    {
                        Append(owner.Memory.Span[..length]);
                    }
                    finally
                    {
                        owner.Dispose();
                    }

                    TryParse(stage);
                },
                onUpstreamFinish: CompleteStage,
                onUpstreamFailure: FailStage);

            SetHandler(stage._outlet,
                onPull: () => Pull(stage._inlet),
                onDownstreamFinish: _ => CompleteStage());
        }

        private void Append(ReadOnlySpan<byte> data)
        {
            EnsureCapacity(_count + data.Length);

            data.CopyTo(_buffer.Span[_count..]);
            _count += data.Length;
        }

        private void EnsureCapacity(int required)
        {
            if (required <= _buffer.Length)
            {
                return;
            }

            var newSize = Math.Max(required, _buffer.Length * 2);

            var newOwner = _pool.Rent(newSize);

            _buffer.Span.CopyTo(newOwner.Memory.Span);
            _bufferOwner?.Dispose();

            _bufferOwner = newOwner;
            _buffer = newOwner.Memory;
        }

        private void TryParse(Http20DecoderStage stage)
        {
            var frames = new List<Http2Frame>();

            while (true)
            {
                if (_count < 9)
                {
                    break;
                }

                var span = _buffer.Span[.._count];

                var length = (span[0] << 16) | (span[1] << 8) | span[2];

                if (_count < 9 + length)
                {
                    break;
                }

                var type = (FrameType)span[3];
                var flags = span[4];

                var streamId = BinaryPrimitives.ReadInt32BigEndian(span.Slice(5, 4)) & 0x7FFFFFFF;

                var payload = span.Slice(9, length).ToArray();

                ShiftBuffer(9 + length);

                frames.Add(CreateFrame(type, flags, streamId, payload));
            }

            if (frames.Count > 0)
            {
                EmitMultiple(stage._outlet, frames);
            }
            else
            {
                Pull(stage._inlet);
            }
        }

        private static Http2Frame CreateFrame(FrameType type, byte flags, int streamId, byte[] payload)
        {
            return type switch
            {
                FrameType.Data => new DataFrame(streamId, payload, (flags & 0x1) != 0),

                FrameType.Headers => new HeadersFrame(streamId, payload, (flags & 0x1) != 0, (flags & 0x4) != 0),

                FrameType.Continuation => new ContinuationFrame(streamId, payload, (flags & 0x4) != 0),

                FrameType.Ping => new PingFrame(payload, (flags & 0x1) != 0),

                FrameType.Settings => ParseSettings(payload, flags),

                FrameType.WindowUpdate => new WindowUpdateFrame(streamId,
                    (int)(BinaryPrimitives.ReadUInt32BigEndian(payload) & 0x7FFFFFFFu)),

                FrameType.RstStream => new RstStreamFrame(streamId,
                    (Http2ErrorCode)BinaryPrimitives.ReadUInt32BigEndian(payload)),

                FrameType.GoAway => ParseGoAway(payload),

                FrameType.PushPromise => ParsePushPromise(streamId, flags, payload),

                _ => throw new Http2Exception($"Unknown frame type 0x{(byte)type:X2}")
            };
        }

        private static SettingsFrame ParseSettings(byte[] payload, byte flags)
        {
            var isAck = (flags & 0x1) != 0;
            var list = new List<(SettingsParameter, uint)>();

            for (var i = 0; i + 6 <= payload.Length; i += 6)
            {
                var key = (SettingsParameter)BinaryPrimitives.ReadUInt16BigEndian(payload.AsSpan(i));
                var value = BinaryPrimitives.ReadUInt32BigEndian(payload.AsSpan(i + 2));
                list.Add((key, value));
            }

            return new SettingsFrame(list, isAck);
        }

        private static GoAwayFrame ParseGoAway(byte[] payload)
        {
            var lastStream = (int)(BinaryPrimitives.ReadUInt32BigEndian(payload) & 0x7FFFFFFFu);
            var errorCode = (Http2ErrorCode)BinaryPrimitives.ReadUInt32BigEndian(payload.AsSpan(4));
            var debugData = payload.Length > 8 ? payload[8..] : [];
            return new GoAwayFrame(lastStream, errorCode, debugData);
        }

        private static PushPromiseFrame ParsePushPromise(int streamId, byte flags, byte[] payload)
        {
            var promised = (int)(BinaryPrimitives.ReadUInt32BigEndian(payload) & 0x7FFFFFFFu);
            var endHeaders = (flags & 0x4) != 0;
            return new PushPromiseFrame(streamId, promised, payload.AsMemory()[4..], endHeaders);
        }

        private void ShiftBuffer(int consumed)
        {
            _count -= consumed;

            if (_count > 0)
            {
                _buffer.Span.Slice(consumed, _count).CopyTo(_buffer.Span);
            }
        }
    }
}