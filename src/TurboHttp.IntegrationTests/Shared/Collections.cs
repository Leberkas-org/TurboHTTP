namespace TurboHttp.IntegrationTests.Shared;

[CollectionDefinition("Http1Integration")]
public sealed class Http1IntegrationCollection : ICollectionFixture<KestrelFixture>;

[CollectionDefinition("Http2Integration")]
public sealed class Http2IntegrationCollection : ICollectionFixture<KestrelH2Fixture>;

[CollectionDefinition("Http3Integration")]
public sealed class Http3IntegrationCollection : ICollectionFixture<KestrelH3Fixture>;

[CollectionDefinition("TlsIntegration")]
public sealed class TlsIntegrationCollection : ICollectionFixture<KestrelTlsFixture>;
