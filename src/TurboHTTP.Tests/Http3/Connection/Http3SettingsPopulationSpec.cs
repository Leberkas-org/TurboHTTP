using Servus.Akka.Transport;
using TurboHTTP.Protocol.Http3;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.Tests.Http3.Connection;

public sealed class Http3SettingsPopulationSpec
{
    private readonly FakeOps _ops = new();

    private StateMachine CreateMachine(TurboClientOptions? options = null)
    {
        return new StateMachine(options ?? new TurboClientOptions(), _ops);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7.2.4")]
    public void TryBuildControlPreface_should_include_qpack_max_table_capacity()
    {
        var opts = new TurboClientOptions();
        opts.Http3.QpackMaxTableCapacity = 8192;
        var sm = CreateMachine(opts);

        var outbound = sm.TryBuildControlPreface();

        Assert.NotNull(outbound);
        var data = ((MultiplexedData)outbound).Buffer;
        var settings = ExtractSettings(data);
        Assert.Equal(8192L, settings.QpackMaxTableCapacity);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7.2.4")]
    public void TryBuildControlPreface_should_include_qpack_blocked_streams()
    {
        var opts = new TurboClientOptions();
        opts.Http3.QpackBlockedStreams = 50;
        var sm = CreateMachine(opts);

        var outbound = sm.TryBuildControlPreface();

        Assert.NotNull(outbound);
        var data = ((MultiplexedData)outbound).Buffer;
        var settings = ExtractSettings(data);
        Assert.Equal(50L, settings.QpackBlockedStreams);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7.2.4")]
    public void TryBuildControlPreface_should_include_max_field_section_size()
    {
        var opts = new TurboClientOptions();
        opts.Http3.MaxFieldSectionSize = 32768;
        var sm = CreateMachine(opts);

        var outbound = sm.TryBuildControlPreface();

        Assert.NotNull(outbound);
        var data = ((MultiplexedData)outbound).Buffer;
        var settings = ExtractSettings(data);
        Assert.Equal(32768L, settings.MaxFieldSectionSize);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7.2.4")]
    public void TryBuildControlPreface_should_include_all_three_settings()
    {
        var opts = new TurboClientOptions();
        opts.Http3.QpackMaxTableCapacity = 4096;
        opts.Http3.QpackBlockedStreams = 100;
        opts.Http3.MaxFieldSectionSize = 65536;
        var sm = CreateMachine(opts);

        var outbound = sm.TryBuildControlPreface();

        Assert.NotNull(outbound);
        var data = ((MultiplexedData)outbound).Buffer;
        var settings = ExtractSettings(data);
        Assert.Equal(3, settings.AllParameters.Count);
    }

    private static Settings ExtractSettings(TransportBuffer buffer)
    {
        var span = buffer.Span;

        QuicVarInt.TryDecode(span, out _, out var streamTypeBytes);
        span = span[streamTypeBytes..];

        QuicVarInt.TryDecode(span, out var frameType, out var frameTypeBytes);
        span = span[frameTypeBytes..];

        QuicVarInt.TryDecode(span, out var payloadLength, out var payloadLenBytes);
        span = span[payloadLenBytes..];

        var payload = span[..(int)payloadLength];
        return Settings.Deserialize(payload);
    }
}
