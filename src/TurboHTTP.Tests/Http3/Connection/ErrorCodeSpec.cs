using TurboHTTP.Protocol.Http3;

namespace TurboHTTP.Tests.Http3.Connection;

public sealed class ErrorCodeSpec
{
    [Theory]
    [Trait("RFC", "RFC9114-8.1")]
    [InlineData(Http3ErrorCode.NoError, 0x100u)]
    [InlineData(Http3ErrorCode.GeneralProtocolError, 0x101u)]
    [InlineData(Http3ErrorCode.InternalError, 0x102u)]
    [InlineData(Http3ErrorCode.StreamCreationError, 0x103u)]
    [InlineData(Http3ErrorCode.ClosedCriticalStream, 0x104u)]
    [InlineData(Http3ErrorCode.FrameUnexpected, 0x105u)]
    [InlineData(Http3ErrorCode.FrameError, 0x106u)]
    [InlineData(Http3ErrorCode.ExcessiveLoad, 0x107u)]
    [InlineData(Http3ErrorCode.IdError, 0x108u)]
    [InlineData(Http3ErrorCode.SettingsError, 0x109u)]
    [InlineData(Http3ErrorCode.MissingSettings, 0x10au)]
    [InlineData(Http3ErrorCode.RequestRejected, 0x10bu)]
    [InlineData(Http3ErrorCode.RequestCancelled, 0x10cu)]
    [InlineData(Http3ErrorCode.RequestIncomplete, 0x10du)]
    [InlineData(Http3ErrorCode.MessageError, 0x10eu)]
    [InlineData(Http3ErrorCode.ConnectError, 0x10fu)]
    [InlineData(Http3ErrorCode.VersionFallback, 0x110u)]
    public void ErrorCode_HasCorrectValue(Http3ErrorCode code, uint expected)
    {
        Assert.Equal(expected, (uint)code);
    }

    [Fact]
    [Trait("RFC", "RFC9114-8.1")]
    public void AllErrorCodes_AreDefined()
    {
        var values = Enum.GetValues<Http3ErrorCode>();
        Assert.Equal(17, values.Length);
    }

    [Fact]
    [Trait("RFC", "RFC9114-8.1")]
    public void FirstErrorCode_IsNoError()
    {
        var min = Enum.GetValues<Http3ErrorCode>().Min(c => (uint)c);
        Assert.Equal(0x100u, min);
    }

    [Fact]
    [Trait("RFC", "RFC9114-8.1")]
    public void LastErrorCode_IsVersionFallback()
    {
        var max = Enum.GetValues<Http3ErrorCode>().Max(c => (uint)c);
        Assert.Equal(0x110u, max);
    }
}
