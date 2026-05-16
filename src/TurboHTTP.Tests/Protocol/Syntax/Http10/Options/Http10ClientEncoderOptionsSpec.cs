using TurboHTTP.Protocol.Syntax;
using TurboHTTP.Protocol.Syntax.Http10.Options;

namespace TurboHTTP.Tests.Protocol.Syntax.Http10.Options;

public sealed class Http10ClientEncoderOptionsSpec
{
    [Fact(Timeout = 5000)]
    public void Default_should_hold_SharedHttpOptions_Default()
    {
        Assert.Same(SharedHttpOptions.Default, Http10ClientEncoderOptions.Default.Shared);
    }

    [Fact(Timeout = 5000)]
    public void Validate_should_delegate_to_Shared()
    {
        var bad = SharedHttpOptions.Default with { MaxHeaderCount = 0 };
        var opts = Http10ClientEncoderOptions.Default with { Shared = bad };
        Assert.Throws<ArgumentException>(opts.Validate);
    }
}