using Akka.Actor;
using Akka.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace TurboHttp.IntegrationTests;

public class TestKit : IDisposable
{
    protected TestKit()
    {
        var diSetup = DependencyResolverSetup.Create(new ServiceCollection().BuildServiceProvider());
        Sys = ActorSystem.Create(Guid.NewGuid().ToString(), BootstrapSetup.Create().And(diSetup));
    }

    protected ActorSystem Sys { get; }

    public void Dispose()
    {
        Sys.Terminate().Wait(TimeSpan.FromSeconds(5));
    }
}