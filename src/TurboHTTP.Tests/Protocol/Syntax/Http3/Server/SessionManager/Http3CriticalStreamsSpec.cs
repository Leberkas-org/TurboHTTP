using Akka.Actor;
using Akka.Event;
using Servus.Akka.Transport;
using TurboHTTP.Protocol.Syntax.Http3;
using TurboHTTP.Protocol.Syntax.Http3.Options;
using TurboHTTP.Protocol.Syntax.Http3.Server;
using TurboHTTP.Streams;

namespace TurboHTTP.Tests.Protocol.Syntax.Http3.Server.SessionManager;

/// <summary>
/// Unit tests for HTTP/3 Http3ServerSessionManager critical streams and SETTINGS frame.
/// Tests that PreStart() opens control, qpack encoder, and qpack decoder streams,
/// and emits SETTINGS frame on the control stream per RFC 9114.
/// </summary>
public sealed class Http3CriticalStreamsSpec
{
    private sealed class TrackingServerOps : IServerStageOperations
    {
        public List<HttpRequestMessage> Requests { get; } = [];
        public List<ITransportOutbound> Outbound { get; } = [];
        public Dictionary<string, (string Name, TimeSpan Delay)> ScheduledTimers { get; } = [];
        public List<string> CancelledTimers { get; } = [];
        public ILoggingAdapter Log { get; } = NoLogger.Instance;
        public IActorRef StageActor { get; set; } = ActorRefs.Nobody;

        public void OnRequest(HttpRequestMessage request) => Requests.Add(request);

        public void OnOutbound(ITransportOutbound item) => Outbound.Add(item);

        public void OnScheduleTimer(string name, TimeSpan delay) => ScheduledTimers[name] = (name, delay);

        public void OnCancelTimer(string name)
        {
            ScheduledTimers.Remove(name);
            CancelledTimers.Add(name);
        }
    }

    private static Http3ServerSessionManager CreateSM(TrackingServerOps ops)
    {
        var enc = new Http3ServerEncoderOptions { QpackMaxTableCapacity = 0 };
        var dec = new Http3ServerDecoderOptions { MaxConcurrentStreams = 100 };
        return new Http3ServerSessionManager(enc, dec, ops);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-6.2.1")]
    public void PreStart_should_open_control_stream()
    {
        var ops = new TrackingServerOps();
        var sm = CreateSM(ops);

        sm.PreStart();

        var opens = ops.Outbound.OfType<OpenStream>().ToList();
        Assert.Contains(opens, o => o.StreamId.Value == CriticalStreamId.ControlId);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-6.2.1")]
    public void PreStart_should_open_qpack_encoder_stream()
    {
        var ops = new TrackingServerOps();
        var sm = CreateSM(ops);

        sm.PreStart();

        var opens = ops.Outbound.OfType<OpenStream>().ToList();
        Assert.Contains(opens, o => o.StreamId.Value == CriticalStreamId.QpackEncoderId);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-6.2.1")]
    public void PreStart_should_open_qpack_decoder_stream()
    {
        var ops = new TrackingServerOps();
        var sm = CreateSM(ops);

        sm.PreStart();

        var opens = ops.Outbound.OfType<OpenStream>().ToList();
        Assert.Contains(opens, o => o.StreamId.Value == CriticalStreamId.QpackDecoderId);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7.2.4")]
    public void PreStart_should_emit_settings_on_control_stream()
    {
        var ops = new TrackingServerOps();
        var sm = CreateSM(ops);

        sm.PreStart();

        var settingsData = ops.Outbound.OfType<MultiplexedData>()
            .Where(m => m.StreamId == CriticalStreamId.ControlId)
            .ToList();

        Assert.NotEmpty(settingsData);
    }

    [Fact(Timeout = 5000)]
    public void Cleanup_should_dispose_all_streams_and_reset()
    {
        var ops = new TrackingServerOps();
        var sm = CreateSM(ops);

        sm.PreStart();

        // Cleanup should not crash and should reset stream count
        sm.Cleanup();

        Assert.Equal(0, sm.ActiveStreamCount);
    }
}
