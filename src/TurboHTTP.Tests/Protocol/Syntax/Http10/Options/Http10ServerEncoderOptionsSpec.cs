using TurboHTTP.Protocol.Syntax;
using TurboHTTP.Protocol.Syntax.Http10.Options;

namespace TurboHTTP.Tests.Protocol.Syntax.Http10.Options;

public sealed class Http10ServerEncoderOptionsSpec
{
    [Fact(Timeout = 5000)]
    public void Default_should_hold_SharedHttpOptions_Default_and_WriteDateHeader_true()
    {
        var d = Http10ServerEncoderOptions.Default;
        Assert.Same(SharedHttpOptions.Default, d.Shared);
        Assert.True(d.WriteDateHeader);
    }

    [Fact(Timeout = 5000)]
    public void With_should_disable_WriteDateHeader()
    {
        var opts = Http10ServerEncoderOptions.Default with { WriteDateHeader = false };
        Assert.False(opts.WriteDateHeader);
    }

    [Fact(Timeout = 5000)]
    public void Validate_should_reject_null_Shared()
    {
        var opts = Http10ServerEncoderOptions.Default with { Shared = null! };
        Assert.Throws<ArgumentException>(opts.Validate);
    }
}