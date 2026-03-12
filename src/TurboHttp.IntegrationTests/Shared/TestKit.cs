using Akka.Actor;
using Akka.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace TurboHttp.IntegrationTests.Shared;

public class TestKit : IAsyncLifetime
{
    protected TestKit()
    {
        var diSetup = DependencyResolverSetup.Create(new ServiceCollection().BuildServiceProvider());
        Sys = ActorSystem.Create(Guid.NewGuid().ToString(), BootstrapSetup.Create().And(diSetup));
    }

    protected ActorSystem Sys { get; }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await Sys.Terminate().WaitAsync(TimeSpan.FromSeconds(10));
    }
}
