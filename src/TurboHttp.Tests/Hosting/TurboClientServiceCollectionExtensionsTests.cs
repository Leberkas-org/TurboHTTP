using System.Linq;
using System.Reflection;
using Akka.Actor;
using Microsoft.Extensions.DependencyInjection;
using TurboHttp.Client;
using TurboHttp.Hosting;

namespace TurboHttp.Tests.Hosting;

/// <summary>
/// Tests for <see cref="TurboClientServiceCollectionExtensions"/>.
/// </summary>
public sealed class TurboClientServiceCollectionExtensionsTests
{
    [Fact(DisplayName = "AddTurboHttpClientFactory creates ActorSystem named 'turbohttp' when none is registered")]
    public async Task Should_CreateActorSystemNamedTurbohttp_When_NoActorSystemRegistered()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddTurboHttpClientFactory(_ => { });

        // Act
        await using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<ITurboHttpClientFactory>();

        // Assert — inspect the ActorSystem via reflection on the concrete factory type
        var systemField = typeof(TurboHttpClientFactory)
            .GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
            .First(f => f.FieldType == typeof(ActorSystem));
        var system = (ActorSystem)systemField.GetValue(factory)!;

        Assert.Equal("turbohttp", system.Name);

        // Cleanup
        await system.Terminate();
    }

    [Fact(DisplayName = "AddTurboHttpClientFactory reuses an existing ActorSystem from DI")]
    public async Task Should_ReuseExistingActorSystem_When_OneIsRegistered()
    {
        // Arrange
        var existingSystem = ActorSystem.Create("my-system");
        try
        {
            var services = new ServiceCollection();
            services.AddSingleton(existingSystem);
            services.AddTurboHttpClientFactory(_ => { });

            // Act
            await using var provider = services.BuildServiceProvider();
            var factory = provider.GetRequiredService<ITurboHttpClientFactory>();

            // Assert — the factory should have received the exact same ActorSystem instance
            var systemField = typeof(TurboHttpClientFactory)
                .GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
                .First(f => f.FieldType == typeof(ActorSystem));
            var system = (ActorSystem)systemField.GetValue(factory)!;

            Assert.Same(existingSystem, system);
        }
        finally
        {
            await existingSystem.Terminate();
        }
    }
}
