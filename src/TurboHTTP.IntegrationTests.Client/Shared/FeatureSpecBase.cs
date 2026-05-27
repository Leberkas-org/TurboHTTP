using TurboHTTP.Tests.Shared;

namespace TurboHTTP.IntegrationTests.Client.Shared;

public abstract class FeatureSpecBase : IntegrationSpecBase
{
    protected FeatureSpecBase(ServerContainerFixture server, ActorSystemFixture systemFixture)
        : base(server, systemFixture)
    {
    }

    public static TheoryData<ProtocolVariant> AllVariants =>
    [
        new ProtocolVariant(TestHttpVersion.H10, false),
        new ProtocolVariant(TestHttpVersion.H11, false),
        new ProtocolVariant(TestHttpVersion.H10, true),
        new ProtocolVariant(TestHttpVersion.H11, true),
        new ProtocolVariant(TestHttpVersion.H2, true),
        new ProtocolVariant(TestHttpVersion.H3, true)
    ];

    public static TheoryData<ProtocolVariant> PlaintextOnly =>
    [
        new ProtocolVariant(TestHttpVersion.H10, false),
        new ProtocolVariant(TestHttpVersion.H11, false)
    ];

    public static TheoryData<ProtocolVariant> TlsOnly =>
    [
        new ProtocolVariant(TestHttpVersion.H10, true),
        new ProtocolVariant(TestHttpVersion.H11, true),
        new ProtocolVariant(TestHttpVersion.H2, true),
        new ProtocolVariant(TestHttpVersion.H3, true)
    ];

    public static TheoryData<ProtocolVariant> MultiplexProtocols =>
    [
        new ProtocolVariant(TestHttpVersion.H2, true),
        new ProtocolVariant(TestHttpVersion.H3, true)
    ];

    public static TheoryData<ProtocolVariant> KestrelOnly =>
    [
        new ProtocolVariant(TestHttpVersion.H11, false),
        new ProtocolVariant(TestHttpVersion.H2, true)
    ];
}