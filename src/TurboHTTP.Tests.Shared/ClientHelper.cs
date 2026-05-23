using TurboHTTP.Client;
using Akka.Actor;
using Akka.Configuration;
using Akka.DependencyInjection;
using Akka.Hosting.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace TurboHTTP.Tests.Shared;

public sealed class ClientHelper : IAsyncDisposable
{
    private static readonly Config LoggingHocon = ConfigurationFactory.ParseString(
        @"akka.loggers = [""Akka.Hosting.Logging.LoggerFactoryLogger, Akka.Hosting""]");

    private readonly Microsoft.Extensions.DependencyInjection.ServiceProvider _provider;
    private readonly bool _ownsSystem;

    private ClientHelper(Microsoft.Extensions.DependencyInjection.ServiceProvider provider, ITurboHttpClient client,
        bool ownsSystem)
    {
        _provider = provider;
        Client = client;
        _ownsSystem = ownsSystem;
    }

    public ITurboHttpClient Client { get; }

    public static ClientHelper CreateClient(
        int port,
        Version version,
        string scheme = "http",
        ILoggerFactory? loggerFactory = null,
        Action<ITurboHttpClientBuilder>? configure = null,
        ActorSystem? system = null,
        Action<TurboClientOptions>? configureOptions = null,
        string host = "127.0.0.1")
    {
        var services = new ServiceCollection();

        bool ownsSystem;
        if (system is not null)
        {
            ownsSystem = false;
        }
        else
        {
            var diSetup = DependencyResolverSetup.Create(services.BuildServiceProvider());
            var bootstrap = BootstrapSetup.Create();

            if (loggerFactory is not null)
            {
                bootstrap = bootstrap.WithConfig(LoggingHocon);
            }

            var setup = loggerFactory is not null
                ? bootstrap.And(diSetup).And(new LoggerFactorySetup(loggerFactory))
                : bootstrap.And(diSetup);

            system = ActorSystem.Create($"turbohttp-{Guid.NewGuid()}", setup);
            ownsSystem = true;
        }

        services.AddSingleton(system);

        var builder = services.AddTurboHttpClient();

        var options = new TurboClientOptions
        {
            BaseAddress = new Uri($"{scheme}://{host}:{port}"),
            DangerousAcceptAnyServerCertificate = true
        };
        configureOptions?.Invoke(options);
        services.Replace(ServiceDescriptor.Singleton<IOptionsFactory<TurboClientOptions>>(
            new FixedOptionsFactory(options)));

        configure?.Invoke(builder);

        var provider = services.BuildServiceProvider();

        var factory = provider.GetRequiredService<ITurboHttpClientFactory>();
        var client = factory.CreateClient(string.Empty);
        client.BaseAddress = options.BaseAddress;
        client.DefaultRequestVersion = version;
        client.Timeout = TimeSpan.FromMinutes(5);

        return new ClientHelper(provider, client, ownsSystem);
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();

        if (_ownsSystem)
        {
            var system = _provider.GetService<ActorSystem>();
            if (system is not null)
            {
                await system.Terminate().WaitAsync(TimeSpan.FromSeconds(10));
                await system.WhenTerminated.WaitAsync(TimeSpan.FromSeconds(5));
            }
        }

        await _provider.DisposeAsync();
    }

    private sealed class FixedOptionsFactory(TurboClientOptions options) : IOptionsFactory<TurboClientOptions>
    {
        public TurboClientOptions Create(string name) => options;
    }
}