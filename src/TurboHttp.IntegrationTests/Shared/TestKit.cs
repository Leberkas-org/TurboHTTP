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

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        await Sys.Terminate().WaitAsync(TimeSpan.FromSeconds(10));
        await Task.Delay(TimeSpan.FromMilliseconds(250));
    }
}
