using TurboHttp.Protocol.RFC9114;
using Xunit;

namespace TurboHttp.Tests.RFC9114;

public sealed class StreamTypeTests
{
    [Theory(DisplayName = "RFC9114-6.2-ST-001: Stream types have correct RFC-specified values")]
    [InlineData(Http3StreamType.Control, 0x00L)]
    [InlineData(Http3StreamType.Push, 0x01L)]
    [InlineData(Http3StreamType.QpackEncoder, 0x02L)]
    [InlineData(Http3StreamType.QpackDecoder, 0x03L)]
    public void StreamType_HasCorrectValue(Http3StreamType type, long expected)
    {
        Assert.Equal(expected, (long)type);
    }

    [Fact(DisplayName = "RFC9114-6.2-ST-002: All 4 stream types are defined")]
    public void AllStreamTypes_AreDefined()
    {
        var values = Enum.GetValues<Http3StreamType>();
        Assert.Equal(4, values.Length);
    }

    [Fact(DisplayName = "RFC9114-6.2-ST-003: Control stream is type 0x00")]
    public void ControlStream_IsZero()
    {
        Assert.Equal(0x00L, (long)Http3StreamType.Control);
    }

    [Fact(DisplayName = "RFC9114-6.2-ST-004: Push stream is type 0x01")]
    public void PushStream_IsOne()
    {
        Assert.Equal(0x01L, (long)Http3StreamType.Push);
    }

    [Fact(DisplayName = "RFC9114-6.2-ST-005: QPACK encoder stream is type 0x02")]
    public void QpackEncoderStream_IsTwo()
    {
        Assert.Equal(0x02L, (long)Http3StreamType.QpackEncoder);
    }

    [Fact(DisplayName = "RFC9114-6.2-ST-006: QPACK decoder stream is type 0x03")]
    public void QpackDecoderStream_IsThree()
    {
        Assert.Equal(0x03L, (long)Http3StreamType.QpackDecoder);
    }
}
