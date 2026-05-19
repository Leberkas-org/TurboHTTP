using Servus.Akka.Transport;
using TurboHTTP.Client;
using TurboHTTP.Protocol.Syntax.Http3;
using TurboHTTP.Protocol.Syntax.Http3.Client;
using Http3Settings = TurboHTTP.Protocol.Syntax.Http3.Settings;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.Tests.Protocol.Syntax.Http3.Client;

/// <summary>
/// Tests for HTTP/3 SETTINGS frame population during connection preface.
///
/// RFC 9114 §7.2.4 requires SETTINGS frames to be sent at the start of the connection.
/// The new Http3ClientStateMachine API calls TryBuildControlPreface() internally during PreStart(),
/// emitting the SETTINGS to Outbound as MultiplexedData on stream -2 (control stream).
///
/// These tests verify that PreStart() emits SETTINGS with the correct parameters by
/// extracting and parsing the MultiplexedData items.
/// </summary>
public sealed class Http3SettingsPopulationSpec
{
    private readonly FakeOps _ops = new();

    private Http3ClientStateMachine CreateMachine(TurboClientOptions? options = null)
    {
        return new Http3ClientStateMachine(options ?? new TurboClientOptions(), _ops);
    }

    private static void SimulateConnect(Http3ClientStateMachine sm)
    {
        sm.DecodeServerData(new TransportConnected(default!));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7.2.4")]
    public void PreStart_should_emit_qpack_max_table_capacity_setting()
    {
        var opts = new TurboClientOptions
        {
            Http3 =
            {
                QpackMaxTableCapacity = 8192
            }
        };
        var sm = CreateMachine(opts);
        _ops.Outbound.Clear();

        sm.PreStart();
        SimulateConnect(sm);

        var settings = ExtractSettingsFromOutbound(_ops);
        Assert.NotNull(settings);
        Assert.Equal(8192L, settings.QpackMaxTableCapacity);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7.2.4")]
    public void PreStart_should_emit_qpack_blocked_streams_setting()
    {
        var opts = new TurboClientOptions
        {
            Http3 =
            {
                QpackBlockedStreams = 50
            }
        };
        var sm = CreateMachine(opts);
        _ops.Outbound.Clear();

        sm.PreStart();
        SimulateConnect(sm);

        var settings = ExtractSettingsFromOutbound(_ops);
        Assert.NotNull(settings);
        Assert.Equal(50L, settings.QpackBlockedStreams);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7.2.4")]
    public void PreStart_should_emit_max_field_section_size_setting()
    {
        var opts = new TurboClientOptions
        {
            Http3 =
            {
                MaxFieldSectionSize = 32768
            }
        };
        var sm = CreateMachine(opts);
        _ops.Outbound.Clear();

        sm.PreStart();
        SimulateConnect(sm);

        var settings = ExtractSettingsFromOutbound(_ops);
        Assert.NotNull(settings);
        Assert.Equal(32768L, settings.MaxFieldSectionSize);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7.2.4")]
    public void PreStart_should_emit_all_three_settings_when_configured()
    {
        var opts = new TurboClientOptions
        {
            Http3 =
            {
                QpackMaxTableCapacity = 4096,
                QpackBlockedStreams = 100,
                MaxFieldSectionSize = 65536
            }
        };
        var sm = CreateMachine(opts);
        _ops.Outbound.Clear();

        sm.PreStart();
        SimulateConnect(sm);

        var settings = ExtractSettingsFromOutbound(_ops);
        Assert.NotNull(settings);
        // Verify all three are present and correct
        Assert.Equal(4096L, settings.QpackMaxTableCapacity);
        Assert.Equal(100L, settings.QpackBlockedStreams);
        Assert.Equal(65536L, settings.MaxFieldSectionSize);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7.2.4")]
    public void PreStart_should_emit_settings_on_control_stream()
    {
        var sm = CreateMachine();
        _ops.Outbound.Clear();

        sm.PreStart();
        SimulateConnect(sm);

        // Find control stream data (stream ID -2) containing SETTINGS
        var controlStreamData = _ops.Outbound
            .OfType<MultiplexedData>()
            .Where(d => d.StreamId == -2)
            .ToList();

        // Should have at least the SETTINGS frame
        Assert.NotEmpty(controlStreamData);
    }

    private static Http3Settings? ExtractSettingsFromOutbound(FakeOps ops)
    {
        // Find the control stream data (-2) that contains SETTINGS
        var controlStreamData = ops.Outbound
            .OfType<MultiplexedData>()
            .FirstOrDefault(d => d.StreamId == -2);

        if (controlStreamData == null)
        {
            return null;
        }

        var buffer = controlStreamData.Buffer;
        var span = buffer.Span;

        // Skip stream type (0x00 for control)
        QuicVarInt.TryDecode(span, out _, out var streamTypeBytes);
        span = span[streamTypeBytes..];

        // Read frame type
        if (!QuicVarInt.TryDecode(span, out _, out var frameTypeBytes))
        {
            return null;
        }

        span = span[frameTypeBytes..];

        // Read payload length
        if (!QuicVarInt.TryDecode(span, out var payloadLength, out var payloadLenBytes))
        {
            return null;
        }

        span = span[payloadLenBytes..];

        // Extract and deserialize SETTINGS
        var payload = span[..(int)payloadLength];
        return Http3Settings.Deserialize(payload);
    }
}