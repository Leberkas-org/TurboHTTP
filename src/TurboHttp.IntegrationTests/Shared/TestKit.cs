using Akka.Actor;
using Akka.DependencyInjection;
using Akka.Hosting;
using Microsoft.Extensions.DependencyInjection;
using TurboHttp.IO;

namespace TurboHttp.IntegrationTests.Shared;

public class TestKit : IAsyncLifetime
{
    protected TestKit()
    {
        var diSetup = DependencyResolverSetup.Create(new ServiceCollection().BuildServiceProvider());
        Sys = ActorSystem.Create(Guid.NewGuid().ToString(), BootstrapSetup.Create().And(diSetup));

        // Register ClientManager so HostPoolActor.SpawnConnection() can resolve it
        // via Context.GetActor<ClientManager>().
        var clientManager = Sys.ActorOf(Props.Create(() => new ClientManager()), "client-manager");
        ActorRegistry.For(Sys).Register<ClientManager>(clientManager);
    }

    protected ActorSystem Sys { get; }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await Sys.Terminate().WaitAsync(TimeSpan.FromSeconds(10));
        await Task.Delay(TimeSpan.FromMilliseconds(250));
    }
}
