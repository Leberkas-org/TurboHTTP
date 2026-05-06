using TurboHTTP.Protocol.Http3;

namespace TurboHTTP.Tests.Http3.Connection;

public sealed class ErrorCodeSpec
{
    [Theory(Timeout = 5000)]
    [Trait("RFC", "RFC9114-8.1")]
    [InlineData(ErrorCode.NoError, 0x100u)]
    [InlineData(ErrorCode.GeneralProtocolError, 0x101u)]
    [InlineData(ErrorCode.InternalError, 0x102u)]
    [InlineData(ErrorCode.StreamCreationError, 0x103u)]
    [InlineData(ErrorCode.ClosedCriticalStream, 0x104u)]
    [InlineData(ErrorCode.FrameUnexpected, 0x105u)]
    [InlineData(ErrorCode.FrameError, 0x106u)]
    [InlineData(ErrorCode.ExcessiveLoad, 0x107u)]
    [InlineData(ErrorCode.IdError, 0x108u)]
    [InlineData(ErrorCode.SettingsError, 0x109u)]
    [InlineData(ErrorCode.MissingSettings, 0x10au)]
    [InlineData(ErrorCode.RequestRejected, 0x10bu)]
    [InlineData(ErrorCode.RequestCancelled, 0x10cu)]
    [InlineData(ErrorCode.RequestIncomplete, 0x10du)]
    [InlineData(ErrorCode.MessageError, 0x10eu)]
    [InlineData(ErrorCode.ConnectError, 0x10fu)]
    [InlineData(ErrorCode.VersionFallback, 0x110u)]
    internal void ErrorCode_HasCorrectValue(ErrorCode code, uint expected)
    {
        Assert.Equal(expected, (uint)code);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-8.1")]
    public void AllErrorCodes_AreDefined()
    {
        var values = Enum.GetValues<ErrorCode>();
        Assert.Equal(17, values.Length);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-8.1")]
    public void FirstErrorCode_IsNoError()
    {
        var min = Enum.GetValues<ErrorCode>().Min(c => (uint)c);
        Assert.Equal(0x100u, min);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-8.1")]
    public void LastErrorCode_IsVersionFallback()
    {
        var max = Enum.GetValues<ErrorCode>().Max(c => (uint)c);
        Assert.Equal(0x110u, max);
    }
}
