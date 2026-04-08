using Akka.Actor;
using Akka.Configuration;
using Akka.DependencyInjection;
using Akka.Hosting.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TurboHTTP.Internal;

namespace TurboHTTP.IntegrationTests.Shared;

/// <summary>
/// Factory helper that creates <see cref="ITurboHttpClient"/> instances via DI,
/// mirroring how end users consume the client. Implements <see cref="IAsyncDisposable"/>
/// to shut down the underlying <see cref="ActorSystem"/> on cleanup.
/// </summary>
public sealed class ClientHelper : IAsyncDisposable
{
    private static readonly Config LoggingHocon = ConfigurationFactory.ParseString(
        @"akka.loggers = [""Akka.Hosting.Logging.LoggerFactoryLogger, Akka.Hosting""]");

    private readonly Microsoft.Extensions.DependencyInjection.ServiceProvider _provider;
    private readonly ITurboHttpClient _client;
    private readonly bool _ownsSystem;

    private ClientHelper(Microsoft.Extensions.DependencyInjection.ServiceProvider provider, ITurboHttpClient client,
        bool ownsSystem)
    {
        _provider = provider;
        _client = client;
        _ownsSystem = ownsSystem;
    }

    /// <summary>The configured <see cref="ITurboHttpClient"/> instance.</summary>
    public ITurboHttpClient Client => _client;

    /// <summary>
    /// Creates a new <see cref="ClientHelper"/> with a fully configured TurboHttp client.
    /// </summary>
    /// <param name="port">The port the test server is listening on.</param>
    /// <param name="version">The HTTP version to use (e.g. <c>new Version(1, 1)</c>).</param>
    /// <param name="scheme">The URI scheme (<c>"http"</c> or <c>"https"</c>). Defaults to <c>"http"</c>.</param>
    /// <param name="loggerFactory">Optional logger factory — when provided, registers it in DI
    /// so the Akka logging bridge picks it up.</param>
    /// <param name="configure">Optional additional builder configuration.</param>
    /// <param name="system">TBD</param>
    public static ClientHelper CreateClient(
        int port,
        Version version,
        string scheme = "http",
        ILoggerFactory? loggerFactory = null,
        Action<ITurboHttpClientBuilder>? configure = null,
        ActorSystem? system = null)
    {
        var services = new ServiceCollection();

        bool ownsSystem;
        if (system is not null)
        {
            // Use the externally provided system — do not terminate it on dispose.
            ownsSystem = false;
        }
        else
        {
            // Create an ActorSystem with DependencyResolver so that Servus.Akka
            // ResolveActor<T> works inside TurboClientStreamManager.
            var diSetup = DependencyResolverSetup.Create(services.BuildServiceProvider());
            var dispatcherConfig = TurboHttpDispatchers.CreateConfig(
                TurboClientOptions.DefaultMaxEndpointSubstreams);
            var bootstrap = BootstrapSetup.Create();

            if (loggerFactory is not null)
                bootstrap = bootstrap.WithConfig(LoggingHocon.WithFallback(dispatcherConfig));
            else
                bootstrap = bootstrap.WithConfig(dispatcherConfig);

            var setup = loggerFactory is not null
                ? bootstrap.And(diSetup).And(new LoggerFactorySetup(loggerFactory))
                : bootstrap.And(diSetup);

            system = ActorSystem.Create($"turbohttp-{Guid.NewGuid()}", setup);
            ownsSystem = true;
        }

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
        client.Timeout = TimeSpan.FromMinutes(5);

        return new ClientHelper(provider, client, ownsSystem);
    }

    public async ValueTask DisposeAsync()
    {
        if (_ownsSystem)
        {
            var system = _provider.GetService<ActorSystem>();
            if (system is not null)
            {
                // Terminate() initiates shutdown; WhenTerminated completes when
                // all actors are stopped and the system is fully torn down.
                await system.Terminate().WaitAsync(TimeSpan.FromSeconds(10));
                await system.WhenTerminated.WaitAsync(TimeSpan.FromSeconds(5));

                // Allow Akka dispatcher threads to fully wind down before the
                // next ActorSystem is created. Without this, lingering threads
                // from the terminated system can interfere with the new one.
                await Task.Delay(TimeSpan.FromMilliseconds(250));
            }
        }

        // Dispose sends Shutdown to the owner actor (fires KillSwitch, starts drain)
        // and completes both channels. Call this first so the actor begins shutting down.
        _client.Dispose();

        // Wait for the owner actor to fully stop. GracefulStop sends Shutdown
        // (idempotent — already sent by Dispose) and blocks until the actor's
        // PostStop has run, so all stream actors are cleanly stopped before the
        // next test materialises a new pipeline on the shared ActorSystem.
        // The actor's internal safety timeout is 5s, so 6s is sufficient.
        if (!_ownsSystem && _client is TurboHttpClient concrete)
        {
            try
            {
                await concrete.Manager.WhenTerminatedAsync(TimeSpan.FromSeconds(6));
            }
            catch
            {
                // Actor may already be stopped or system shutting down — fine.
            }
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