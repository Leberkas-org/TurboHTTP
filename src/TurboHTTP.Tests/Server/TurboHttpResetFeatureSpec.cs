using TurboHTTP.Server.Context.Features;

namespace TurboHTTP.Tests.Server;

public sealed class TurboHttpResetFeatureSpec
{
    [Fact(Timeout = 5000)]
    public void Reset_should_invoke_callback_with_error_code()
    {
        var capturedCode = -1;
        var feature = new TurboHttpResetFeature(code => capturedCode = code);

        feature.Reset(8);

        Assert.Equal(8, capturedCode);
    }

    [Fact(Timeout = 5000)]
    public void Reset_should_pass_zero_error_code()
    {
        var capturedCode = -1;
        var feature = new TurboHttpResetFeature(code => capturedCode = code);

        feature.Reset(0);

        Assert.Equal(0, capturedCode);
    }
}
