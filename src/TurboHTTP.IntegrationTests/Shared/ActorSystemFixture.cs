using Akka.Actor;
using Akka.Configuration;
using Akka.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TurboHTTP.Diagnostics;

namespace TurboHTTP.IntegrationTests.Shared;

/// <summary>
/// xunit collection fixture that creates and owns exactly one <see cref="ActorSystem"/>
/// for the lifetime of a test collection. All test classes in the collection share the
/// same system, eliminating per-test create/destroy overhead.
/// </summary>
public sealed class ActorSystemFixture : IAsyncLifetime
{
    private static readonly Config QuietConfig = ConfigurationFactory.ParseString(
        "akka.loglevel = WARNING");

    public ActorSystem System { get; private set; } = null!;

    public ValueTask InitializeAsync()
    {
        var loggerFactory = LoggerFactory.Create(b =>
        {
            b.AddConsole();
            b.SetMinimumLevel(LogLevel.Information);
        });

        TurboTrace.Configure(
            new LoggerTraceListener(loggerFactory, TurboTraceCategory.All, TurboTraceLevel.Info),
            TurboTraceCategory.All,
            TurboTraceLevel.Info);

        var services = new ServiceCollection();
        var diSetup = DependencyResolverSetup.Create(services.BuildServiceProvider());
        var bootstrap = BootstrapSetup.Create().WithConfig(QuietConfig);

        var setup = bootstrap.And(diSetup);
        System = ActorSystem.Create($"turbohttp-shared-{Guid.NewGuid()}", setup);
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await System.Terminate().WaitAsync(TimeSpan.FromSeconds(30));
        await System.WhenTerminated.WaitAsync(TimeSpan.FromSeconds(30));
    }
}