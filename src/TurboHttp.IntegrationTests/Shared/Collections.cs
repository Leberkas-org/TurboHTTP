namespace TurboHttp.IntegrationTests.Shared;

[CollectionDefinition("H10")]
public sealed class H10IntegrationCollection : ICollectionFixture<KestrelFixture>;

[CollectionDefinition("H11")]
public sealed class H11IntegrationCollection : ICollectionFixture<KestrelFixture>;

[CollectionDefinition("H2")]
public sealed class H2IntegrationCollection : ICollectionFixture<KestrelH2Fixture>;

[CollectionDefinition("H3")]
public sealed class H3IntegrationCollection : ICollectionFixture<KestrelH3Fixture>;

[CollectionDefinition("TlsIntegration")]
public sealed class TlsIntegrationCollection : ICollectionFixture<KestrelTlsFixture>;