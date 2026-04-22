using TurboHTTP.Internal;
using TurboHTTP.Protocol.Http3;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.Tests.Http3.Connection;

public sealed class Http3DecoderStreamSpec
{
    private readonly FakeOps _ops = new();

    private StateMachine CreateMachine(TurboClientOptions? options = null)
    {
        return new StateMachine(options ?? new TurboClientOptions(), _ops);
    }

    [Fact(Timeout = 5000)]
    public void FlushDecoderInstructions_should_not_emit_when_no_instructions_pending()
    {
        var sm = CreateMachine();

        sm.FlushDecoderInstructions();

        var decoderItems = _ops.Outbound
            .OfType<Http3NetworkBuffer>()
            .Where(t => t.StreamTypeValue == (long)StreamType.QpackDecoder)
            .ToList();
        Assert.Empty(decoderItems);
    }

    [Fact(Timeout = 5000)]
    public void FlushDecoderInstructions_should_prepend_stream_type_on_first_emission()
    {
        var sm = CreateMachine();

        // Apply an encoder instruction to bump the decoder's insert count,
        // so WriteInsertCountIncrement has something to emit.
        var encoderInstr = BuildInsertInstruction("x-test", "value");
        sm.TableSync.ApplyEncoderInstructions(encoderInstr);

        sm.FlushDecoderInstructions();

        var decoderItems = _ops.Outbound
            .OfType<Http3NetworkBuffer>()
            .Where(t => t.StreamTypeValue == (long)StreamType.QpackDecoder)
            .ToList();
        Assert.Single(decoderItems);

        var buf = decoderItems[0];
        // First byte should be 0x03 (decoder stream type)
        Assert.Equal(0x03, buf.Span[0]);
        buf.Dispose();
    }

    [Fact(Timeout = 5000)]
    public void FlushDecoderInstructions_should_omit_stream_type_on_subsequent_emissions()
    {
        var sm = CreateMachine();

        // First emission — has preface
        sm.TableSync.ApplyEncoderInstructions(BuildInsertInstruction("x-first", "v1"));
        sm.FlushDecoderInstructions();
        var first = ExtractDecoderBuffer(_ops, 0);
        Assert.Equal(0x03, first.Span[0]);
        first.Dispose();

        // Second emission — no preface
        _ops.Outbound.Clear();
        sm.TableSync.ApplyEncoderInstructions(BuildInsertInstruction("x-second", "v2"));
        sm.FlushDecoderInstructions();

        var second = ExtractDecoderBuffer(_ops, 0);
        // Should NOT start with 0x03 (no preface on second emission)
        Assert.NotEqual(0x03, second.Span[0]);
        second.Dispose();
    }

    [Fact(Timeout = 5000)]
    public void OnConnectionLost_should_reset_decoder_preface_flag()
    {
        var sm = CreateMachine();

        // First emission with preface
        sm.TableSync.ApplyEncoderInstructions(BuildInsertInstruction("x-test", "value"));
        sm.FlushDecoderInstructions();
        _ops.Outbound.Clear();

        // Reconnect cycle
        sm.EncodeRequest(new HttpRequestMessage(HttpMethod.Get, "https://example.com/"));
        sm.OnConnectionLost();

        // After reconnect, preface should be re-emitted
        sm.TableSync.ApplyEncoderInstructions(BuildInsertInstruction("x-test2", "value2"));
        sm.FlushDecoderInstructions();

        var buf = ExtractDecoderBuffer(_ops, 0);
        Assert.Equal(0x03, buf.Span[0]);
        buf.Dispose();
    }

    [Fact(Timeout = 5000)]
    public void OnConnectionLost_should_reset_qpack_table_sync_state()
    {
        var sm = CreateMachine();

        // Build up some dynamic table state
        sm.TableSync.ApplyEncoderInstructions(BuildInsertInstruction("x-test", "value"));
        Assert.True(sm.TableSync.InsertCount > 0);

        sm.EncodeRequest(new HttpRequestMessage(HttpMethod.Get, "https://example.com/"));
        sm.OnConnectionLost();

        // After reset, insert count should be back to zero
        Assert.Equal(0, sm.TableSync.InsertCount);
        Assert.Equal(0, sm.TableSync.KnownReceivedCount);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-4.4.3")]
    public void ProcessQpackEncoderBytes_should_emit_insert_count_increment()
    {
        var sm = CreateMachine();

        var encoderInstr = BuildInsertInstruction("x-test", "value");
        sm.ProcessQpackEncoderBytes(encoderInstr);

        var decoderItems = _ops.Outbound
            .OfType<Http3NetworkBuffer>()
            .Where(t => t.StreamTypeValue == (long)StreamType.QpackDecoder)
            .ToList();
        Assert.Single(decoderItems);

        var buf = decoderItems[0];
        Assert.True(buf.Length > 1); // preface (0x03) + at least 1 ICR byte
        Assert.Equal(0x03, buf.Span[0]);
        buf.Dispose();
    }

    private static NetworkBuffer ExtractDecoderBuffer(FakeOps ops, int index)
    {
        var items = ops.Outbound
            .OfType<Http3NetworkBuffer>()
            .Where(t => t.StreamTypeValue == (long)StreamType.QpackDecoder)
            .ToList();
        return items[index];
    }

    private static byte[] BuildInsertInstruction(string name, string value)
    {
        var nameBytes = System.Text.Encoding.ASCII.GetBytes(name);
        var valueBytes = System.Text.Encoding.ASCII.GetBytes(value);

        // Insert With Literal Name: prefix 0b01, no Huffman
        var buf = new byte[2 + nameBytes.Length + 1 + valueBytes.Length];
        var pos = 0;

        // 0b01xxxxxx — Insert With Literal Name, H=0, name length
        buf[pos++] = (byte)(0x40 | nameBytes.Length);
        nameBytes.CopyTo(buf.AsSpan(pos));
        pos += nameBytes.Length;

        // Value: H=0, value length
        buf[pos++] = (byte)valueBytes.Length;
        valueBytes.CopyTo(buf.AsSpan(pos));

        return buf;
    }
}