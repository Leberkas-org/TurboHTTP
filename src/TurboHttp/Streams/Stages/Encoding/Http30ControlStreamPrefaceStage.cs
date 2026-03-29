using System.Buffers;
using Akka.Event;
using Akka.Streams;
using Akka.Streams.Stage;
using TurboHttp.Internal;
using TurboHttp.Protocol.RFC9114;

namespace TurboHttp.Streams.Stages.Encoding;

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
/// Uses <see cref="Http3ControlStream.OpenLocalStream"/> to generate the preface bytes.
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
            var controlStream = new Http3ControlStream();
            var preface = controlStream.OpenLocalStream(_stage._localSettings);

            var owner = MemoryPool<byte>.Shared.Rent(preface.Length);
            ((ReadOnlySpan<byte>)preface).CopyTo(owner.Memory.Span);

            var dataItem = new DataItem(owner, preface.Length);
            Emit(_stage._out, new Http3OutputTaggedItem(dataItem, OutputStreamType.Control));
        }
    }
}
