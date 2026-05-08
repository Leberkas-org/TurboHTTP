[assembly: AssemblyFixture(typeof(TurboHTTP.IntegrationTests.Container.Shared.ServerContainerFixture))]
[assembly: AssemblyFixture(typeof(TurboHTTP.IntegrationTests.Container.Shared.ActorSystemFixture))]

namespace TurboHTTP.IntegrationTests.Container.Shared;

[CollectionDefinition("H10")]
public sealed class H10IntegrationCollection;

[CollectionDefinition("H11")]
public sealed class H11IntegrationCollection;

[CollectionDefinition("H2")]
public sealed class H2IntegrationCollection;

[CollectionDefinition("H3")]
public sealed class H3IntegrationCollection;

[CollectionDefinition("TLS")]
public sealed class TlsIntegrationCollection;

[CollectionDefinition("Features")]
public sealed class FeaturesIntegrationCollection;
