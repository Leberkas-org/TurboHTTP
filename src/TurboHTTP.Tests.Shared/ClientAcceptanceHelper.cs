using Akka.Actor;
using Akka.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using TurboHTTP.Streams;

namespace TurboHTTP.Tests.Shared;

internal sealed class ClientAcceptanceHelper : IAsyncDisposable
{
    private readonly Microsoft.Extensions.DependencyInjection.ServiceProvider _provider;

    private ClientAcceptanceHelper(Microsoft.Extensions.DependencyInjection.ServiceProvider provider,
        ITurboHttpClient client)
    {
        _provider = provider;
        Client = client;
    }

    public ITurboHttpClient Client { get; }

    public static ClientAcceptanceHelper Create(
        TransportRegistry transports,
        Version version,
        Action<ITurboHttpClientBuilder>? configure = null,
        Action<TurboClientOptions>? configureOptions = null)
    {
        var services = new ServiceCollection();

        var diSetup = DependencyResolverSetup.Create(services.BuildServiceProvider());
        var bootstrap = BootstrapSetup.Create();
        var system = ActorSystem.Create($"acceptance-{Guid.NewGuid()}", bootstrap.And(diSetup));

        services.AddSingleton(system);

        var builder = services.AddTurboHttpClient();

        var options = new TurboClientOptions
        {
            BaseAddress = new Uri("http://fake.test")
        };
        configureOptions?.Invoke(options);
        services.Replace(ServiceDescriptor.Singleton<IOptionsFactory<TurboClientOptions>>(
            new FixedOptionsFactory(options)));

        configure?.Invoke(builder);

        var provider = services.BuildServiceProvider();

        var factory = (TurboHttpClientFactory)provider.GetRequiredService<ITurboHttpClientFactory>();
        var client = factory.CreateClient(string.Empty, transports);
        client.BaseAddress = options.BaseAddress;
        client.DefaultRequestVersion = version;
        client.Timeout = TimeSpan.FromSeconds(10);

        return new ClientAcceptanceHelper(provider, client);
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();

        var system = _provider.GetService<ActorSystem>();
        if (system is not null)
        {
            await system.Terminate().WaitAsync(TimeSpan.FromSeconds(10));
            await system.WhenTerminated.WaitAsync(TimeSpan.FromSeconds(5));
        }

        await _provider.DisposeAsync();
    }

    private sealed class FixedOptionsFactory(TurboClientOptions options) : IOptionsFactory<TurboClientOptions>
    {
        public TurboClientOptions Create(string name) => options;
    }
}