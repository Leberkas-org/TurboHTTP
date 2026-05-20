using System.Net.Security;

namespace TurboHTTP.Server;

public sealed class TurboTlsCallbackOptions
{
    public required Func<TurboTlsCallbackContext, ValueTask<SslServerAuthenticationOptions>> OnConnection { get; init; }
    public TimeSpan HandshakeTimeout { get; init; } = TimeSpan.FromSeconds(10);
}
