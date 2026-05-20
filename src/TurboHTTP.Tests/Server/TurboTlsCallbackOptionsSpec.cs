using System.Net.Security;
using TurboHTTP.Server;

namespace TurboHTTP.Tests.Server;

public sealed class TurboTlsCallbackOptionsSpec
{
    [Fact(Timeout = 5000)]
    public void TurboTlsCallbackOptions_should_default_handshake_timeout_to_10_seconds()
    {
        var options = new TurboTlsCallbackOptions
        {
            OnConnection = _ => new ValueTask<SslServerAuthenticationOptions>(
                new SslServerAuthenticationOptions())
        };
        Assert.Equal(TimeSpan.FromSeconds(10), options.HandshakeTimeout);
    }

    [Fact(Timeout = 5000)]
    public void TurboTlsCallbackOptions_should_allow_setting_handshake_timeout()
    {
        var options = new TurboTlsCallbackOptions
        {
            OnConnection = _ => new ValueTask<SslServerAuthenticationOptions>(
                new SslServerAuthenticationOptions()),
            HandshakeTimeout = TimeSpan.FromSeconds(30)
        };
        Assert.Equal(TimeSpan.FromSeconds(30), options.HandshakeTimeout);
    }
}
