using TurboHTTP.Protocol.Syntax;
using TurboHTTP.Protocol.Syntax.Http10.Options;

namespace TurboHTTP.Tests.Protocol.Syntax.Http10.Options;

public sealed class Http10ServerDecoderOptionsSpec
{
    [Fact(Timeout = 5000)]
    public void Default_should_hold_SharedHttpOptions_Default()
    {
        Assert.Same(SharedHttpOptions.Default, Http10ServerDecoderOptions.Default.Shared);
    }

    [Fact(Timeout = 5000)]
    public void Validate_should_reject_null_Shared()
    {
        var opts = Http10ServerDecoderOptions.Default with { Shared = null! };
        Assert.Throws<ArgumentException>(opts.Validate);
    }
}