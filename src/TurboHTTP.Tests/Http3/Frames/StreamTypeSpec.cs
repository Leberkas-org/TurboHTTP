using TurboHTTP.Protocol.Http3;

namespace TurboHTTP.Tests.Http3.Frames;

public sealed class StreamTypeSpec
{
    [Theory(Timeout = 5000)]
    [Trait("RFC", "RFC9114-6.2")]
    [InlineData(StreamType.Control, 0x00L)]
    [InlineData(StreamType.Push, 0x01L)]
    [InlineData(StreamType.QpackEncoder, 0x02L)]
    [InlineData(StreamType.QpackDecoder, 0x03L)]
    internal void StreamType_should_have_correct_value(StreamType type, long expected)
    {
        Assert.Equal(expected, (long)type);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-6.2")]
    public void StreamType_should_have_all_types_defined()
    {
        var values = Enum.GetValues<StreamType>();
        Assert.Equal(4, values.Length);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-6.2")]
    public void StreamType_should_be_zero_when_control_stream()
    {
        Assert.Equal(0x00L, (long)StreamType.Control);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-6.2")]
    public void StreamType_should_be_one_when_push_stream()
    {
        Assert.Equal(0x01L, (long)StreamType.Push);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-6.2")]
    public void StreamType_should_be_two_when_qpack_encoder_stream()
    {
        Assert.Equal(0x02L, (long)StreamType.QpackEncoder);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-6.2")]
    public void StreamType_should_be_three_when_qpack_decoder_stream()
    {
        Assert.Equal(0x03L, (long)StreamType.QpackDecoder);
    }
}