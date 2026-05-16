using TurboHTTP.Protocol;
using TurboHTTP.Protocol.Syntax.Http3.Qpack;

namespace TurboHTTP.Tests.Protocol.Syntax.Http3.Qpack;

public sealed class QpackDynamicTableActivationSpec
{
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-3.2.3")]
    public void Encoder_should_not_insert_when_capacity_is_zero()
    {
        var encoder = new QpackEncoder(maxTableCapacity: 0);
        var headers = new List<(string, string)> { ("x-custom", "value1") };

        var buf = new byte[8192];
        var writer = SpanWriter.Create(buf);
        encoder.Encode(headers, ref writer);

        Assert.Equal(0, encoder.DynamicTable.Count);
        Assert.Equal(0, encoder.EncoderInstructions.Length);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-3.2.3")]
    public void Encoder_should_insert_after_SetMaxCapacity()
    {
        var encoder = new QpackEncoder(maxTableCapacity: 0);

        encoder.SetMaxCapacity(4096);

        var headers = new List<(string, string)> { ("x-custom", "value1") };
        var buf = new byte[8192];
        var writer = SpanWriter.Create(buf);
        encoder.Encode(headers, ref writer);

        Assert.Equal(1, encoder.DynamicTable.Count);
        Assert.True(encoder.EncoderInstructions.Length > 0);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-4.3.1")]
    public void SetMaxCapacity_should_emit_set_dynamic_table_capacity_instruction()
    {
        var encoder = new QpackEncoder(maxTableCapacity: 0);

        encoder.SetMaxCapacity(4096);

        var instructions = encoder.EncoderInstructions;
        Assert.True(instructions.Length > 0);

        // §4.3.1: Set Dynamic Table Capacity starts with 001xxxxx (0x20 prefix)
        Assert.Equal(0x20, instructions.Span[0] & 0xE0);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-3.2.3")]
    public void SetMaxCapacity_to_zero_should_disable_dynamic_table()
    {
        var encoder = new QpackEncoder(maxTableCapacity: 4096);

        encoder.SetMaxCapacity(0);

        var headers = new List<(string, string)> { ("x-custom", "value1") };
        var buf = new byte[8192];
        var writer = SpanWriter.Create(buf);
        encoder.Encode(headers, ref writer);

        Assert.Equal(0, encoder.DynamicTable.Count);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-3.2.3")]
    public void TableSync_should_activate_encoder_via_UpdateEncoderCapacity()
    {
        var sync = new QpackTableSync(encoderMaxCapacity: 0, configuredEncoderLimit: 4096);

        sync.UpdateEncoderCapacity(8192);

        var headers = new List<(string, string)> { ("x-custom", "value1") };
        sync.Encoder.Encode(headers);

        Assert.Equal(1, sync.Encoder.DynamicTable.Count);
        Assert.True(sync.Encoder.EncoderInstructions.Length > 0);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-3.2.3")]
    public void UpdateEncoderCapacity_should_cap_at_configured_limit()
    {
        var sync = new QpackTableSync(encoderMaxCapacity: 0, configuredEncoderLimit: 2048);

        sync.UpdateEncoderCapacity(16384);

        Assert.Equal(2048, sync.Encoder.DynamicTable.Capacity);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-3.2.3")]
    public void UpdateEncoderCapacity_should_use_peer_value_when_smaller()
    {
        var sync = new QpackTableSync(encoderMaxCapacity: 0, configuredEncoderLimit: 16384);

        sync.UpdateEncoderCapacity(1024);

        Assert.Equal(1024, sync.Encoder.DynamicTable.Capacity);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-3.2.3")]
    public void UpdateEncoderCapacity_should_noop_when_peer_sends_zero()
    {
        var sync = new QpackTableSync(encoderMaxCapacity: 0, configuredEncoderLimit: 4096);

        sync.UpdateEncoderCapacity(0);

        var headers = new List<(string, string)> { ("x-custom", "value1") };
        var buf = new byte[8192];
        var writer = SpanWriter.Create(buf);
        sync.Encoder.Encode(headers, ref writer);

        Assert.Equal(0, sync.Encoder.DynamicTable.Count);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-3.2.3")]
    public void UpdateEncoderCapacity_should_noop_when_configured_limit_is_zero()
    {
        var sync = new QpackTableSync(encoderMaxCapacity: 0, configuredEncoderLimit: 0);

        sync.UpdateEncoderCapacity(4096);

        var headers = new List<(string, string)> { ("x-custom", "value1") };
        var buf = new byte[8192];
        var writer = SpanWriter.Create(buf);
        sync.Encoder.Encode(headers, ref writer);

        Assert.Equal(0, sync.Encoder.DynamicTable.Count);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-3.2.3")]
    public void Reset_should_return_encoder_to_disabled_state()
    {
        var sync = new QpackTableSync(encoderMaxCapacity: 0, configuredEncoderLimit: 4096);

        sync.UpdateEncoderCapacity(4096);
        Assert.Equal(4096, sync.Encoder.DynamicTable.Capacity);

        sync.Reset();

        var headers = new List<(string, string)> { ("x-custom", "value1") };
        var buf = new byte[8192];
        var writer = SpanWriter.Create(buf);
        sync.Encoder.Encode(headers, ref writer);

        Assert.Equal(0, sync.Encoder.DynamicTable.Count);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-3.2.3")]
    public void Encoder_should_roundtrip_after_activation()
    {
        var sync = new QpackTableSync(encoderMaxCapacity: 0, configuredEncoderLimit: 4096);
        sync.UpdateEncoderCapacity(4096);

        var headers = new List<(string, string)>
        {
            ("x-request-id", "abc-123"),
            ("x-trace-id", "trace-456"),
        };

        var encoded = sync.Encoder.Encode(headers);

        // Flush encoder instructions first so decoder can sync
        // Skip the Set Dynamic Table Capacity instruction from SetMaxCapacity
        // (already applied), apply only the insert instructions from Encode
        sync.ProcessEncoderInstructions(sync.Encoder.EncoderInstructions.Span);

        var decoded = sync.Decoder.Decode(encoded.Span);

        Assert.Equal(2, decoded.Count);
        Assert.Equal("x-request-id", decoded[0].Name);
        Assert.Equal("abc-123", decoded[0].Value);
        Assert.Equal("x-trace-id", decoded[1].Name);
        Assert.Equal("trace-456", decoded[1].Value);
    }
}