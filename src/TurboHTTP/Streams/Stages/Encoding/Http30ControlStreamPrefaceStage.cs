using System.Buffers;
using Akka.Event;
using Akka.Streams;
using Akka.Streams.Stage;
using TurboHTTP.Internal;
using TurboHTTP.Protocol.Http3;

namespace TurboHTTP.Streams.Stages.Encoding;

/// <summary>
/// Emits the HTTP/3 control stream preface (stream type VarInt 0x00 + SETTINGS frame)
/// eagerly on PreStart, then passes all subsequent upstream items through unchanged.
/// The preface is wrapped in an <see cref="Http3OutputTaggedItem"/> with
/// <see cref="OutputStreamType.Control"/> so the demux stage can route it
/// to the correct QUIC unidirectional stream.
/// </summary>
/// <remarks>
/// RFC 9114 §6.2.1: Each side of an HTTP/3 connection MUST initiate a single control stream.
/// The first frame sent on the control stream MUST be a SETTINGS frame.
/// Generates the preface bytes inline: stream type VarInt (0x00) + SETTINGS frame.
/// </remarks>
public sealed class Http30ControlStreamPrefaceStage : GraphStage<FlowShape<IOutputItem, IOutputItem>>
{
    private readonly Inlet<IOutputItem> _in = new("Http30ControlStreamPreface.In");
    private readonly Outlet<IOutputItem> _out = new("Http30ControlStreamPreface.Out");

    private readonly Http3Settings? _localSettings;

    public Http30ControlStreamPrefaceStage(Http3Settings? localSettings = null)
    {
        _localSettings = localSettings;
    }

    public override FlowShape<IOutputItem, IOutputItem> Shape => new(_in, _out);

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        => new Logic(this);

    private sealed class Logic : GraphStageLogic
    {
        private readonly Http30ControlStreamPrefaceStage _stage;

        public Logic(Http30ControlStreamPrefaceStage stage) : base(stage.Shape)
        {
            _stage = stage;

            SetHandler(stage._out, onPull: () => Pull(stage._in));

            SetHandler(stage._in,
                onPush: () => Push(stage._out, Grab(stage._in)),
                onUpstreamFinish: CompleteStage,
                onUpstreamFailure: ex =>
                {
                    Log.Warning("Http30ControlStreamPrefaceStage: Upstream failure absorbed: {0}", ex.Message);
                    Log.Debug("Http30ControlStreamPrefaceStage: Failing stage due to upstream error: {0}", ex.Message);
                    FailStage(ex);
                });
        }

        public override void PreStart()
        {
            var settings = _stage._localSettings ?? new Http3Settings();
            var settingsFrame = settings.ToFrame();

            // Stream type prefix (0x00 for control) + SETTINGS frame
            var streamTypeSize = QuicVarInt.EncodedLength((long)Http3StreamType.Control);
            var frameSize = settingsFrame.SerializedSize;
            var totalSize = streamTypeSize + frameSize;
            using var owner = MemoryPool<byte>.Shared.Rent(totalSize);
            var span = owner.Memory.Span;

            var written = QuicVarInt.Encode((long)Http3StreamType.Control, span);
            span = span[written..];
            settingsFrame.WriteTo(ref span);

            var buf = NetworkBuffer.Rent(totalSize);
            owner.Memory.Span[..totalSize].CopyTo(buf.FullMemory.Span);
            buf.Length = totalSize;

            Emit(_stage._out, new Http3OutputTaggedItem(buf, OutputStreamType.Control));
        }
    }
}
