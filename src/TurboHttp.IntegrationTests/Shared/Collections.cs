[assembly: AssemblyFixture(typeof(TurboHttp.IntegrationTests.Shared.ServerFixture))]
[assembly: AssemblyFixture(typeof(TurboHttp.IntegrationTests.Shared.ActorSystemFixture))]

namespace TurboHttp.IntegrationTests.Shared;

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

[CollectionDefinition("Logging", DisableParallelization = true)]
public sealed class LoggingIntegrationCollection;
