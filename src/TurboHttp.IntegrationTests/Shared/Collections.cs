namespace TurboHttp.IntegrationTests.Shared;

[CollectionDefinition("H10")]
public sealed class H10IntegrationCollection : ICollectionFixture<KestrelFixture>, ICollectionFixture<ActorSystemFixture>;

[CollectionDefinition("H11")]
public sealed class H11IntegrationCollection : ICollectionFixture<KestrelFixture>, ICollectionFixture<ActorSystemFixture>;

[CollectionDefinition("H2")]
public sealed class H2IntegrationCollection : ICollectionFixture<KestrelH2Fixture>, ICollectionFixture<ActorSystemFixture>;

[CollectionDefinition("H3")]
public sealed class H3IntegrationCollection : ICollectionFixture<KestrelH3Fixture>, ICollectionFixture<ActorSystemFixture>;

[CollectionDefinition("TlsIntegration")]
public sealed class TlsIntegrationCollection : ICollectionFixture<KestrelTlsFixture>, ICollectionFixture<ActorSystemFixture>;