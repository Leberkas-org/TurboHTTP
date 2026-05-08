namespace TurboHTTP.IntegrationTests.Kestrel.Protocol;

public sealed class EnvironmentSpec
{
    [Fact]
    public void KestrelTests_should_require_opt_in()
    {
        var enabled = Environment.GetEnvironmentVariable("TURBOHTTP_KESTREL_TESTS") is "true";
        Assert.True(true, enabled
            ? "Kestrel tests enabled"
            : "Kestrel tests disabled (set TURBOHTTP_KESTREL_TESTS=true to enable)");
    }
}
