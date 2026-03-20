using System.Reflection;
using Akka.Actor;
using Akka.DependencyInjection;
using Akka.Hosting;
using Microsoft.Extensions.DependencyInjection;
using TurboHttp.Client;
using TurboHttp.IO;

namespace TurboHttp.Tests.Client;

/// <summary>
/// Verifies that fire-and-forget tasks in <see cref="TurboClientStreamManager"/>
/// have exception continuations so that faulted tasks never cause
/// <see cref="TaskScheduler.UnobservedTaskException"/>.
/// </summary>
public sealed class TurboClientStreamManagerTests
{
    private static ActorSystem CreateSystem(string name)
    {
        var diSetup = DependencyResolverSetup.Create(new ServiceCollection().BuildServiceProvider());
        var system = ActorSystem.Create(name, BootstrapSetup.Create().And(diSetup));
        var clientManager = system.ActorOf(Props.Create(() => new ClientManager()), "client-manager");
        ActorRegistry.For(system).Register<ClientManager>(clientManager);
        return system;
    }

    private static TurboClientStreamManager GetManager(TurboHttpClient client)
    {
        var field = typeof(TurboHttpClient)
            .GetField("_manager", BindingFlags.NonPublic | BindingFlags.Instance)!;
        return (TurboClientStreamManager)field.GetValue(client)!;
    }

    [Fact(DisplayName = "TASK-020-001: faulted pump task does not raise UnobservedTaskException")]
    public async Task PumpTask_Fault_DoesNotRaiseUnobservedException()
    {
        var system = CreateSystem("test-stream-manager-pump-fault");
        var unobservedExceptions = new List<UnobservedTaskExceptionEventArgs>();

        EventHandler<UnobservedTaskExceptionEventArgs> handler = (_, e) =>
        {
            e.SetObserved();
            unobservedExceptions.Add(e);
        };

        TaskScheduler.UnobservedTaskException += handler;

        try
        {
            var client = new TurboHttpClient(new TurboClientOptions(), system);
            var manager = GetManager(client);

            // Completing the request writer with an exception causes ReadAllAsync()
            // inside PumpRequestsAsync to throw, faulting the pump task.
            manager.Requests.Complete(new InvalidOperationException("simulated pump fault"));

            // Allow the pump task to run and fault.
            await Task.Delay(200);

            // Trigger GC to collect the faulted task and fire UnobservedTaskException
            // if the exception was not observed by a continuation.
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            // Allow finalizer callbacks to be dispatched.
            await Task.Delay(100);

            Assert.Empty(unobservedExceptions);
        }
        finally
        {
            TaskScheduler.UnobservedTaskException -= handler;
            await system.Terminate();
        }
    }
}
