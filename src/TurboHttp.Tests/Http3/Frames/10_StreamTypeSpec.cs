using TurboHttp.Protocol.Http3;

namespace TurboHttp.Tests.Http3.Frames;

public sealed class StreamTypeSpec
{
    [Theory]
    [Trait("RFC", "RFC9114-6.2")]
    [InlineData(Http3StreamType.Control, 0x00L)]
    [InlineData(Http3StreamType.Push, 0x01L)]
    [InlineData(Http3StreamType.QpackEncoder, 0x02L)]
    [InlineData(Http3StreamType.QpackDecoder, 0x03L)]
    public void StreamType_HasCorrectValue(Http3StreamType type, long expected)
    {
        Assert.Equal(expected, (long)type);
    }

    [Fact]
    [Trait("RFC", "RFC9114-6.2")]
    public void AllStreamTypes_AreDefined()
    {
        var values = Enum.GetValues<Http3StreamType>();
        Assert.Equal(4, values.Length);
    }

    [Fact]
    [Trait("RFC", "RFC9114-6.2")]
    public void ControlStream_IsZero()
    {
        Assert.Equal(0x00L, (long)Http3StreamType.Control);
    }

    [Fact]
    [Trait("RFC", "RFC9114-6.2")]
    public void PushStream_IsOne()
    {
        Assert.Equal(0x01L, (long)Http3StreamType.Push);
    }

    [Fact]
    [Trait("RFC", "RFC9114-6.2")]
    public void QpackEncoderStream_IsTwo()
    {
        Assert.Equal(0x02L, (long)Http3StreamType.QpackEncoder);
    }

    [Fact]
    [Trait("RFC", "RFC9114-6.2")]
    public void QpackDecoderStream_IsThree()
    {
        Assert.Equal(0x03L, (long)Http3StreamType.QpackDecoder);
    }
}
