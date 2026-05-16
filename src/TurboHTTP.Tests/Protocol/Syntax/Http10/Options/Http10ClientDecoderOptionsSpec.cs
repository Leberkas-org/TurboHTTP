using TurboHTTP.Protocol.Syntax;
using TurboHTTP.Protocol.Syntax.Http10.Options;

namespace TurboHTTP.Tests.Protocol.Syntax.Http10.Options;

public sealed class Http10ClientDecoderOptionsSpec
{
    [Fact(Timeout = 5000)]
    public void Default_should_hold_SharedHttpOptions_Default()
    {
        Assert.Same(SharedHttpOptions.Default, Http10ClientDecoderOptions.Default.Shared);
    }

    [Fact(Timeout = 5000)]
    public void Validate_should_delegate_to_Shared()
    {
        var bad = SharedHttpOptions.Default with { StreamingThreshold = -1 };
        var opts = Http10ClientDecoderOptions.Default with { Shared = bad };
        Assert.Throws<ArgumentException>(opts.Validate);
    }

    [Fact(Timeout = 5000)]
    public void Validate_should_reject_null_Shared()
    {
        var opts = Http10ClientDecoderOptions.Default with { Shared = null! };
        Assert.Throws<ArgumentException>(opts.Validate);
    }
}