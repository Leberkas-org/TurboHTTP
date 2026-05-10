namespace TurboHTTP.IntegrationTests.Kestrel.Shared;

[Collection("Features")]
public abstract class FeatureSpecBase
{
    private readonly ServerFixture _server;
    private readonly ActorSystemFixture _systemFixture;

    protected FeatureSpecBase(ServerFixture server, ActorSystemFixture systemFixture)
    {
        _server = server;
        _systemFixture = systemFixture;
    }

    public static TheoryData<HttpProtocol> Protocols =>
    [
        HttpProtocol.H10,
        HttpProtocol.H11,
        HttpProtocol.H2,
        HttpProtocol.Tls
    ];

    public static TheoryData<HttpProtocol> PlaintextProtocols =>
    [
        HttpProtocol.H10,
        HttpProtocol.H11
    ];

    public static TheoryData<HttpProtocol> TlsProtocols =>
    [
        HttpProtocol.H2,
        HttpProtocol.Tls
    ];

    protected ClientHelper CreateClient(
        HttpProtocol protocol,
        Action<ITurboHttpClientBuilder>? configure = null,
        Action<TurboClientOptions>? configureOptions = null)
    {
        if (!_server.IsEnabled)
        {
            Assert.Skip("Kestrel tests disabled. Set TURBOHTTP_KESTREL_TESTS=true to enable.");
        }

        return protocol switch
        {
            HttpProtocol.H10 => ClientHelper.CreateClient(
                _server.H1Port,
                new Version(1, 0),
                system: _systemFixture.System,
                configure: configure,
                configureOptions: configureOptions),

            HttpProtocol.H11 => ClientHelper.CreateClient(
                _server.HttpPort,
                new Version(1, 1),
                system: _systemFixture.System,
                configure: configure,
                configureOptions: configureOptions),

            HttpProtocol.H2 => ClientHelper.CreateClient(
                _server.HttpsPort,
                new Version(2, 0),
                scheme: "https",
                system: _systemFixture.System,
                configure: configure,
                configureOptions: configureOptions),

            HttpProtocol.Tls => ClientHelper.CreateClient(
                _server.HttpsPort,
                new Version(1, 1),
                scheme: "https",
                system: _systemFixture.System,
                configure: configure,
                configureOptions: configureOptions),

            _ => throw new ArgumentOutOfRangeException(nameof(protocol))
        };
    }
}
