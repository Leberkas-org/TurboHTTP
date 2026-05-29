using TurboHTTP.IntegrationTests.Server.Shared;

[assembly: AssemblyFixture(typeof(TurboServerFixture))]

namespace TurboHTTP.IntegrationTests.Server.Shared;

[CollectionDefinition("ServerStress", DisableParallelization = true)]
public sealed class ServerStressCollection;

[CollectionDefinition("Infrastructure", DisableParallelization = true)]
public sealed class InfrastructureCollection;
