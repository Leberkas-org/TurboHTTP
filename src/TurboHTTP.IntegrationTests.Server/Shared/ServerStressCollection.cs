using TurboHTTP.Tests.Shared;

[assembly: AssemblyFixture(typeof(ActorSystemFixture))]

namespace TurboHTTP.IntegrationTests.Server.Shared;

[CollectionDefinition("ServerStress", DisableParallelization = true)]
public sealed class ServerStressCollection;

[CollectionDefinition("Infrastructure", DisableParallelization = true)]
public sealed class InfrastructureCollection;
