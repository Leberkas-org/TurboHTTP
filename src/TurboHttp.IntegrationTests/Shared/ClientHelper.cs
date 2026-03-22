using Akka.Actor;
using Akka.DependencyInjection;
using Akka.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using TurboHttp.Client;
using TurboHttp.Transport;

namespace TurboHttp.IntegrationTests.Shared;

/// <summary>
/// Factory helper that creates <see cref="ITurboHttpClient"/> instances via DI,
/// mirroring how end users consume the client. Implements <see cref="IAsyncDisposable"/>
/// to shut down the underlying <see cref="ActorSystem"/> on cleanup.
/// </summary>
public sealed class ClientHelper : IAsyncDisposable
{
    private readonly Microsoft.Extensions.DependencyInjection.ServiceProvider _provider;
    private readonly ITurboHttpClient _client;

    private ClientHelper(Microsoft.Extensions.DependencyInjection.ServiceProvider provider, ITurboHttpClient client)
    {
        _provider = provider;
        _client = client;
    }

    /// <summary>The configured <see cref="ITurboHttpClient"/> instance.</summary>
    public ITurboHttpClient Client => _client;

    /// <summary>
    /// Creates a new <see cref="ClientHelper"/> with a fully configured TurboHttp client.
    /// </summary>
    /// <param name="port">The port the test server is listening on.</param>
    /// <param name="version">The HTTP version to use (e.g. <c>new Version(1, 1)</c>).</param>
    /// <param name="scheme">The URI scheme (<c>"http"</c> or <c>"https"</c>). Defaults to <c>"http"</c>.</param>
    /// <param name="configure">Optional additional builder configuration.</param>
    public static ClientHelper CreateClient(
        int port,
        Version version,
        string scheme = "http",
        Action<ITurboHttpClientBuilder>? configure = null)
    {
        var services = new ServiceCollection();

        // Create an ActorSystem with DependencyResolver so that Servus.Akka
        // ResolveActor<T> works inside TurboClientStreamManager.
        var diSetup = DependencyResolverSetup.Create(services.BuildServiceProvider());
        var system = ActorSystem.Create($"turbohttp-{Guid.NewGuid()}", BootstrapSetup.Create().And(diSetup));

        // Register ClientManager so HostPoolActor.SpawnConnection() can resolve it.
        var clientManager = system.ActorOf(Props.Create(() => new ClientManager()), "client-manager");
        ActorRegistry.For(system).Register<ClientManager>(clientManager);

        // Register the pre-configured ActorSystem so the factory picks it up
        // instead of creating its own (which lacks DI setup).
        services.AddSingleton(system);

        var builder = services.AddTurboHttpClient();

        // TurboClientOptions uses init-only properties, so we replace the
        // IOptionsFactory<TurboClientOptions> with one that returns a pre-built instance.
        var options = new TurboClientOptions
        {
            BaseAddress = new Uri($"{scheme}://127.0.0.1:{port}"),
            DangerousAcceptAnyServerCertificate = true
        };
        services.Replace(ServiceDescriptor.Singleton<IOptionsFactory<TurboClientOptions>>(
            new FixedOptionsFactory(options)));

        configure?.Invoke(builder);

        var provider = services.BuildServiceProvider();

        var factory = provider.GetRequiredService<ITurboHttpClientFactory>();
        var client = factory.CreateClient(string.Empty);
        client.BaseAddress = options.BaseAddress;
        client.DefaultRequestVersion = version;

        return new ClientHelper(provider, client);
    }

    public async ValueTask DisposeAsync()
    {
        var system = _provider.GetService<ActorSystem>();
        if (system is not null)
        {
            await system.Terminate().WaitAsync(TimeSpan.FromSeconds(10));
        }

        await _provider.DisposeAsync();
    }

    /// <summary>
    /// Options factory that always returns a pre-built <see cref="TurboClientOptions"/> instance,
    /// bypassing the init-only property restriction in the standard Configure pipeline.
    /// </summary>
    private sealed class FixedOptionsFactory(TurboClientOptions options) : IOptionsFactory<TurboClientOptions>
    {
        public TurboClientOptions Create(string name) => options;
    }
}
