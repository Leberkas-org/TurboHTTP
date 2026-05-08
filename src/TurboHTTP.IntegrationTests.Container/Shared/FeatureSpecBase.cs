namespace TurboHTTP.IntegrationTests.Container.Shared;

[Collection("Features")]
public abstract class FeatureSpecBase
{
    private readonly ServerContainerFixture _server;
    private readonly ActorSystemFixture _systemFixture;

    protected FeatureSpecBase(ServerContainerFixture server, ActorSystemFixture systemFixture)
    {
        _server = server;
        _systemFixture = systemFixture;
    }

    public static TheoryData<HttpProtocol> Protocols => new()
    {
        HttpProtocol.H10,
        HttpProtocol.H11,
        HttpProtocol.H2,
        HttpProtocol.Tls
    };

    public static TheoryData<HttpProtocol> PlaintextProtocols => new()
    {
        HttpProtocol.H10,
        HttpProtocol.H11
    };

    public static TheoryData<HttpProtocol> TlsProtocols => new()
    {
        HttpProtocol.H2,
        HttpProtocol.Tls
    };

    protected ClientHelper CreateClient(
        HttpProtocol protocol,
        Action<ITurboHttpClientBuilder>? configure = null,
        Action<TurboClientOptions>? configureOptions = null)
    {
        if (!_server.IsDockerAvailable)
        {
            Assert.Skip("Docker is not available.");
        }

        return protocol switch
        {
            HttpProtocol.H10 => ClientHelper.CreateClient(
                _server.HttpPort,
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

            HttpProtocol.H2 => CreateTlsClient(new Version(2, 0), configure, configureOptions),

            HttpProtocol.Tls => CreateTlsClient(new Version(1, 1), configure, configureOptions),

            _ => throw new ArgumentOutOfRangeException(nameof(protocol))
        };
    }

    private ClientHelper CreateTlsClient(
        Version version,
        Action<ITurboHttpClientBuilder>? configure = null,
        Action<TurboClientOptions>? configureOptions = null)
    {
        if (_server.HttpsPort == 0)
        {
            Assert.Skip("Nginx TLS proxy is not available.");
        }

        return ClientHelper.CreateClient(
            _server.HttpsPort,
            version,
            scheme: "https",
            system: _systemFixture.System,
            host: "localhost",
            configure: configure,
            configureOptions: configureOptions);
    }
}
