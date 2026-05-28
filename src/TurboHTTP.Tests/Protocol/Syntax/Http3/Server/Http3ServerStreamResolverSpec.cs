using Servus.Akka.Transport;
using TurboHTTP.Protocol.Syntax.Http3;
using TurboHTTP.Protocol.Syntax.Http3.Server;

namespace TurboHTTP.Tests.Protocol.Syntax.Http3.Server;

public sealed class Http3ServerStreamResolverSpec
{
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-6.2.1")]
    public void Resolve_should_detect_control_stream()
    {
        var resolver = new ServerStreamResolver();
        var buffer = BuildStreamTypeBuffer(StreamType.Control);
        resolver.OnServerStreamOpened(1);

        var result = resolver.Resolve(1, buffer);

        Assert.Equal(CriticalStreamId.ControlId, result.LogicalStreamId);
        Assert.Null(result.Buffer);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-6.2.1")]
    public void Resolve_should_detect_qpack_encoder_stream()
    {
        var resolver = new ServerStreamResolver();
        var buffer = BuildStreamTypeBuffer(StreamType.QpackEncoder);
        resolver.OnServerStreamOpened(3);

        var result = resolver.Resolve(3, buffer);

        Assert.Equal(CriticalStreamId.QpackEncoderId, result.LogicalStreamId);
        Assert.Null(result.Buffer);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-6.2.1")]
    public void Resolve_should_detect_qpack_decoder_stream()
    {
        var resolver = new ServerStreamResolver();
        var buffer = BuildStreamTypeBuffer(StreamType.QpackDecoder);
        resolver.OnServerStreamOpened(5);

        var result = resolver.Resolve(5, buffer);

        Assert.Equal(CriticalStreamId.QpackDecoderId, result.LogicalStreamId);
        Assert.Null(result.Buffer);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-6.2.1")]
    public void Resolve_should_reject_duplicate_control_stream()
    {
        var resolver = new ServerStreamResolver();
        var buffer1 = BuildStreamTypeBuffer(StreamType.Control);
        var buffer2 = BuildStreamTypeBuffer(StreamType.Control);
        resolver.OnServerStreamOpened(1);
        resolver.OnServerStreamOpened(3);

        resolver.Resolve(1, buffer1);

        var ex = Assert.Throws<HttpProtocolException>(() => resolver.Resolve(3, buffer2));
        Assert.Contains("Duplicate stream type", ex.Message);
        Assert.Contains("Control", ex.Message);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-6.2.1")]
    public void Resolve_should_reject_duplicate_qpack_encoder_stream()
    {
        var resolver = new ServerStreamResolver();
        var buffer1 = BuildStreamTypeBuffer(StreamType.QpackEncoder);
        var buffer2 = BuildStreamTypeBuffer(StreamType.QpackEncoder);
        resolver.OnServerStreamOpened(1);
        resolver.OnServerStreamOpened(3);

        resolver.Resolve(1, buffer1);

        var ex = Assert.Throws<HttpProtocolException>(() => resolver.Resolve(3, buffer2));
        Assert.Contains("Duplicate stream type", ex.Message);
        Assert.Contains("QpackEncoder", ex.Message);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-6.2.1")]
    public void Resolve_should_reject_duplicate_qpack_decoder_stream()
    {
        var resolver = new ServerStreamResolver();
        var buffer1 = BuildStreamTypeBuffer(StreamType.QpackDecoder);
        var buffer2 = BuildStreamTypeBuffer(StreamType.QpackDecoder);
        resolver.OnServerStreamOpened(1);
        resolver.OnServerStreamOpened(3);

        resolver.Resolve(1, buffer1);

        var ex = Assert.Throws<HttpProtocolException>(() => resolver.Resolve(3, buffer2));
        Assert.Contains("Duplicate stream type", ex.Message);
        Assert.Contains("QpackDecoder", ex.Message);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-6.2")]
    public void Resolve_should_trim_stream_type_and_preserve_remaining_data()
    {
        var resolver = new ServerStreamResolver();
        var extraData = new byte[] { 0xAA, 0xBB, 0xCC };
        var buffer = BuildStreamTypeBuffer(StreamType.Control, extraData);
        resolver.OnServerStreamOpened(1);

        var result = resolver.Resolve(1, buffer);

        Assert.Equal(CriticalStreamId.ControlId, result.LogicalStreamId);
        Assert.NotNull(result.Buffer);
        Assert.Equal(3, result.Buffer.Length);
        Assert.Equal(extraData, result.Buffer.Span.ToArray());
        result.Buffer.Dispose();
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-6.2")]
    public void Resolve_should_return_null_buffer_when_no_remaining_data()
    {
        var resolver = new ServerStreamResolver();
        var buffer = BuildStreamTypeBuffer(StreamType.Control);
        resolver.OnServerStreamOpened(1);

        var result = resolver.Resolve(1, buffer);

        Assert.Equal(CriticalStreamId.ControlId, result.LogicalStreamId);
        Assert.Null(result.Buffer);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-6.2")]
    public void Resolve_bidirectional_stream_should_pass_through()
    {
        var resolver = new ServerStreamResolver();
        var extraData = new byte[] { 0x01, 0x02, 0x03 };
        var buffer = BuildStreamTypeBuffer(StreamType.Control, extraData);

        var result = resolver.Resolve(4, buffer);

        Assert.Equal(4L, result.LogicalStreamId);
        Assert.NotNull(result.Buffer);
        Assert.Equal(4, result.Buffer.Length);
        result.Buffer.Dispose();
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-6.2.1")]
    public void Reset_should_clear_all_state()
    {
        var resolver = new ServerStreamResolver();
        var buffer1 = BuildStreamTypeBuffer(StreamType.Control);
        var buffer2 = BuildStreamTypeBuffer(StreamType.Control);
        resolver.OnServerStreamOpened(1);

        resolver.Resolve(1, buffer1);
        resolver.Reset();
        resolver.OnServerStreamOpened(3);

        var result = resolver.Resolve(3, buffer2);

        Assert.Equal(CriticalStreamId.ControlId, result.LogicalStreamId);
        Assert.Null(result.Buffer);
    }

    private static TransportBuffer BuildStreamTypeBuffer(StreamType streamType, byte[]? extraData = null)
    {
        var typeBytes = new byte[8];
        var typeLen = QuicVarInt.Encode((long)streamType, typeBytes);
        var totalSize = typeLen + (extraData?.Length ?? 0);
        var buffer = TransportBuffer.Rent(totalSize);
        typeBytes.AsSpan(0, typeLen).CopyTo(buffer.FullMemory.Span);
        extraData?.CopyTo(buffer.FullMemory.Span[typeLen..]);

        buffer.Length = totalSize;
        return buffer;
    }
}
