using Servus.Akka.Transport;
using TurboHTTP.Protocol.Syntax.Http3;
using TurboHTTP.Protocol.Syntax.Http3.Options;
using TurboHTTP.Protocol.Syntax.Http3.Server;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.Tests.Protocol.Syntax.Http3.Server.SessionManager;

public sealed class Http3CriticalStreamsSpec
{

    private static Http3ServerSessionManager CreateSM(FakeServerOps ops)
    {
        var enc = new Http3ServerEncoderOptions { QpackMaxTableCapacity = 0 };
        var dec = new Http3ServerDecoderOptions { MaxConcurrentStreams = 100 };
        return new Http3ServerSessionManager(enc, dec, ops);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-6.2.1")]
    public void PreStart_should_open_control_stream()
    {
        var ops = new FakeServerOps();
        var sm = CreateSM(ops);

        sm.PreStart();

        var opens = ops.Outbound.OfType<OpenStream>().ToList();
        Assert.Contains(opens, o => o.StreamId.Value == CriticalStreamId.ControlId);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-6.2.1")]
    public void PreStart_should_open_qpack_encoder_stream()
    {
        var ops = new FakeServerOps();
        var sm = CreateSM(ops);

        sm.PreStart();

        var opens = ops.Outbound.OfType<OpenStream>().ToList();
        Assert.Contains(opens, o => o.StreamId.Value == CriticalStreamId.QpackEncoderId);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-6.2.1")]
    public void PreStart_should_open_qpack_decoder_stream()
    {
        var ops = new FakeServerOps();
        var sm = CreateSM(ops);

        sm.PreStart();

        var opens = ops.Outbound.OfType<OpenStream>().ToList();
        Assert.Contains(opens, o => o.StreamId.Value == CriticalStreamId.QpackDecoderId);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7.2.4")]
    public void PreStart_should_emit_settings_on_control_stream()
    {
        var ops = new FakeServerOps();
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
        var ops = new FakeServerOps();
        var sm = CreateSM(ops);

        sm.PreStart();

        // Cleanup should not crash and should reset stream count
        sm.Cleanup();

        Assert.Equal(0, sm.ActiveStreamCount);
    }
}