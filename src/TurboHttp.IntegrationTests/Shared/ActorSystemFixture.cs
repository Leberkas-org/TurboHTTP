using Akka.Actor;
using Akka.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace TurboHttp.IntegrationTests.Shared;

/// <summary>
/// xunit collection fixture that creates and owns exactly one <see cref="ActorSystem"/>
/// for the lifetime of a test collection. All test classes in the collection share the
/// same system, eliminating per-test create/destroy overhead.
/// </summary>
public sealed class ActorSystemFixture : IAsyncLifetime
{
    public ActorSystem System { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        var services = new ServiceCollection();
        var diSetup = DependencyResolverSetup.Create(services.BuildServiceProvider());
        var bootstrap = BootstrapSetup.Create().And(diSetup);
        System = ActorSystem.Create($"turbohttp-shared-{Guid.NewGuid()}", bootstrap);
        await Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await System.Terminate().WaitAsync(TimeSpan.FromSeconds(30));
        await System.WhenTerminated.WaitAsync(TimeSpan.FromSeconds(5));
    }
}
