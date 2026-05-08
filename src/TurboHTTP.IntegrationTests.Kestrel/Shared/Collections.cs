[assembly: AssemblyFixture(typeof(TurboHTTP.IntegrationTests.Kestrel.Shared.ServerFixture))]
[assembly: AssemblyFixture(typeof(TurboHTTP.IntegrationTests.Kestrel.Shared.ActorSystemFixture))]

namespace TurboHTTP.IntegrationTests.Kestrel.Shared;

[CollectionDefinition("H10")]
public sealed class H10Collection;

[CollectionDefinition("H11")]
public sealed class H11Collection;

[CollectionDefinition("H2")]
public sealed class H2Collection;

[CollectionDefinition("TLS")]
public sealed class TlsCollection;

[CollectionDefinition("Features")]
public sealed class FeaturesCollection;
