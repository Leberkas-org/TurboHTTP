using System;
using System.Threading;
using Akka.Actor;
using Akka.DependencyInjection;
using Akka.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TurboHttp.Client;
using TurboHttp.IO;

namespace TurboHttp.Tests.Hosting;

/// <summary>
/// Verifies that <see cref="TurboHttpClientFactory.CreateClient"/> gives each call an
/// independent copy of the options snapshot so that per-call configuration or future
/// <see cref="IOptionsMonitor{TOptions}"/> reloads do not corrupt other clients.
/// </summary>
public sealed class TurboHttpClientFactoryTests
{
    private sealed class FakeOptionsMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue => value;
        public T Get(string? name) => value;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }

    private static ActorSystem CreateSystem(string name)
    {
        var diSetup = DependencyResolverSetup.Create(new ServiceCollection().BuildServiceProvider());
        var system = ActorSystem.Create(name, BootstrapSetup.Create().And(diSetup));
        var clientManager = system.ActorOf(Props.Create(() => new ClientManager()), "client-manager");
        ActorRegistry.For(system).Register<ClientManager>(clientManager);
        return system;
    }

    [Fact(DisplayName = "Factory-001: each CreateClient call receives an independent options copy")]
    public async System.Threading.Tasks.Task Should_GiveIndependentCopies_To_EachCreateClientCall()
    {
        var sharedOptions = new TurboClientOptions();
        var monitor = new FakeOptionsMonitor<TurboClientOptions>(sharedOptions);
        var system = CreateSystem("test-factory-isolation");
        try
        {
            var factory = new TurboHttpClientFactory(monitor, system);

            TurboClientOptions? captured1 = null;
            TurboClientOptions? captured2 = null;

            factory.CreateClient(o => captured1 = o);
            factory.CreateClient(o => captured2 = o);

            Assert.NotNull(captured1);
            Assert.NotNull(captured2);

            // Each call must yield a distinct object
            Assert.NotSame(captured1, captured2);

            // Neither copy should be the shared CurrentValue itself
            Assert.NotSame(sharedOptions, captured1);
            Assert.NotSame(sharedOptions, captured2);
        }
        finally
        {
            await system.Terminate();
        }
    }

    [Fact(DisplayName = "Factory-002: configure callback never receives the CurrentValue reference directly")]
    public async System.Threading.Tasks.Task Should_NotPassCurrentValue_Directly_ToConfigureCallback()
    {
        var sharedOptions = new TurboClientOptions();
        var monitor = new FakeOptionsMonitor<TurboClientOptions>(sharedOptions);
        var system = CreateSystem("test-factory-nomutate");
        try
        {
            var factory = new TurboHttpClientFactory(monitor, system);

            // If the factory passes CurrentValue directly to configure, any mutation
            // of reference-type members (e.g. ClientCertificates) would affect all clients.
            // The fix ensures configure always receives a shallow copy, so CurrentValue
            // cannot be mutated through the callback.
            TurboClientOptions? received = null;
            factory.CreateClient(o => received = o);

            Assert.NotNull(received);
            // The copy passed to configure must not be the shared CurrentValue instance
            Assert.NotSame(monitor.CurrentValue, received);
        }
        finally
        {
            await system.Terminate();
        }
    }
}
