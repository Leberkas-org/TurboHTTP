using TurboHTTP.Client;

namespace TurboHTTP.IntegrationTests.Shared;

public abstract class IntegrationSpecBase : IAsyncLifetime
{
    private readonly ActorSystemFixture _systemFixture;
    private ClientHelper? _helper;

    protected IntegrationSpecBase(
        ServerContainerFixture server,
        ActorSystemFixture systemFixture)
    {
        Server = server;
        _systemFixture = systemFixture;
    }

    protected virtual ProtocolVariant? Variant => null;

    protected ITurboHttpClient Client => _helper!.Client;

    protected static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    private ServerContainerFixture Server { get; }

    public ValueTask InitializeAsync()
    {
        if (Variant is not null)
        {
            SkipIfUnavailable(Variant);
            _helper = BuildClient(Variant);
        }

        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (_helper is not null)
        {
            await _helper.DisposeAsync();
        }
    }

    protected ClientHelper CreateClient(
        ProtocolVariant variant,
        Action<ITurboHttpClientBuilder>? configure = null,
        Action<TurboClientOptions>? configureOptions = null)
    {
        SkipIfUnavailable(variant);
        return BuildClient(variant, configure, configureOptions);
    }

    private void SkipIfUnavailable(ProtocolVariant variant)
    {
        if (!Server.IsBackendAvailable)
        {
            Assert.Skip("No test backend available.");
        }

        if (variant.Tls && variant.Version != TestHttpVersion.H3 && Server.HttpsPort == 0)
        {
            Assert.Skip("TLS is not available on this backend.");
        }

        if (variant.Version == TestHttpVersion.H3 && !Server.IsQuicAvailable)
        {
            Assert.Skip("QUIC is not available.");
        }

        if (variant.Version == TestHttpVersion.H10 && variant.Tls && !Server.IsHttp10TlsSupported)
        {
            Assert.Skip("HTTP/1.0 over TLS is not supported by this backend.");
        }
    }

    private ClientHelper BuildClient(
        ProtocolVariant variant,
        Action<ITurboHttpClientBuilder>? configure = null,
        Action<TurboClientOptions>? configureOptions = null)
    {
        var (port, scheme, host) = variant switch
        {
            { Tls: false } => (Server.HttpPort, "http", "127.0.0.1"),
            { Version: TestHttpVersion.H3 } => (Server.QuicPort, "https", "localhost"),
            _ => (Server.HttpsPort, "https", "localhost")
        };

        var version = variant.Version switch
        {
            TestHttpVersion.H10 => new Version(1, 0),
            TestHttpVersion.H11 => new Version(1, 1),
            TestHttpVersion.H2 => new Version(2, 0),
            TestHttpVersion.H3 => new Version(3, 0),
            _ => throw new ArgumentOutOfRangeException()
        };

        return ClientHelper.CreateClient(
            port, version, scheme: scheme,
            system: _systemFixture.System,
            host: host,
            configure: configure,
            configureOptions: configureOptions);
    }
}