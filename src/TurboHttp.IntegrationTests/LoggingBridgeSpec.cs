using System.Collections.Concurrent;
using Akka.Actor;
using Akka.Configuration;
using Akka.DependencyInjection;
using Akka.Hosting.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TurboHttp.Diagnostics;
using TurboHttp.IntegrationTests.Shared;
using TurboHttp.Internal;

namespace TurboHttp.IntegrationTests;

/// <summary>
/// Verifies the two logging integration paths that a user would configure:
/// <list type="number">
///   <item>
///     Akka → Microsoft.Extensions.Logging bridge: registering <see cref="ILoggerFactory"/> in DI
///     before calling <see cref="TurboClientServiceCollectionExtensions.AddTurboHttpClient()"/>
///     routes Akka actor log messages (from <c>ClientStreamOwnerActor</c>) through MEL.
///   </item>
///   <item>
///     TurboTrace → MEL via <see cref="TurboTraceExtensions.AddTurboLoggerTracing"/>: pipeline
///     stages emit structured trace events to <c>TurboHttp.Trace.*</c> loggers.
///   </item>
/// </list>
/// </summary>
[Collection("Logging")]
public sealed class LoggingBridgeSpec : IAsyncLifetime
{
    private static readonly Config LoggingHocon = ConfigurationFactory.ParseString("""
        akka.loggers = ["Akka.Hosting.Logging.LoggerFactoryLogger, Akka.Hosting"]
        akka.loglevel = DEBUG
        """);

    private readonly ServerFixture _server;
    private Microsoft.Extensions.DependencyInjection.ServiceProvider? _provider;
    private CapturingLoggerProvider _capture = null!;
    private ITurboHttpClient? _client;

    public LoggingBridgeSpec(ServerFixture server) => _server = server;

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        TurboTrace.Disable();

        if (_client is not null)
        {
            _client.Requests.TryComplete();
            try
            {
                await _client.Responses.Completion.WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch
            {
            }

            _client.Dispose();
        }

        if (_provider is not null)
        {
            var system = _provider.GetService<ActorSystem>();
            if (system is not null)
            {
                await system.Terminate().WaitAsync(TimeSpan.FromSeconds(10));
                await system.WhenTerminated.WaitAsync(TimeSpan.FromSeconds(5));
                await Task.Delay(TimeSpan.FromMilliseconds(250));
            }

            await _provider.DisposeAsync();
        }
    }

    /// <summary>
    /// Builds a fully DI-wired client, mirroring a user's Program.cs / Startup setup.
    /// Registers <see cref="ILoggerFactory"/> and <see cref="ActorSystem"/> as singletons
    /// so that <see cref="ITurboHttpClientFactory"/> picks them up via normal DI resolution.
    /// </summary>
    private ITurboHttpClient BuildClientViaUserDI(bool withTurboTrace = false)
    {
        _capture = new CapturingLoggerProvider();

        var services = new ServiceCollection();

        // User step 1: register logging
        services.AddLogging(b =>
        {
            b.SetMinimumLevel(LogLevel.Debug);
            b.AddProvider(_capture);
        });

        // Register ActorSystem as a DI singleton — uses the same ILoggerFactory that
        // AddLogging() provides, so the Akka→MEL bridge and the capture provider share
        // the exact same factory instance.
        services.AddSingleton<ActorSystem>(sp =>
        {
            var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
            var diSetup = DependencyResolverSetup.Create(sp);
            var dispatcherConfig = TurboHttpDispatchers.CreateConfig(
                TurboClientOptions.DefaultMaxEndpointSubstreams);
            var setup = BootstrapSetup.Create()
                .WithConfig(LoggingHocon.WithFallback(dispatcherConfig))
                .And(diSetup)
                .And(new LoggerFactorySetup(loggerFactory));
            return ActorSystem.Create("turbohttp-bridge-test", setup);
        });

        // User step 2: register TurboHttp client
        services.AddTurboHttpClient(opts =>
        {
            opts.BaseAddress = new Uri($"http://127.0.0.1:{_server.HttpPort}");
            opts.DangerousAcceptAnyServerCertificate = true;
        });

        // User step 3 (optional): route TurboTrace events to MEL
        if (withTurboTrace)
        {
            services.AddTurboLoggerTracing();
        }

        _provider = services.BuildServiceProvider();

        // Eagerly resolve the trace listener so TurboTrace.Configure() is called
        // before the stream materializes on the first request.
        if (withTurboTrace)
        {
            _ = _provider.GetRequiredService<ITurboTraceListener>();
        }

        var factory = _provider.GetRequiredService<ITurboHttpClientFactory>();
        _client = factory.CreateClient(string.Empty);
        _client.BaseAddress = new Uri($"http://127.0.0.1:{_server.HttpPort}");
        _client.DefaultRequestVersion = new Version(1, 1);
        _client.Timeout = TimeSpan.FromMinutes(1);

        return _client;
    }

    [Fact(Timeout = 20000)]
    public async Task Akka_bridge_should_route_stream_creation_message_to_MEL()
    {
        // Verifies that ClientStreamOwnerActor's Info log for "Creating stream instance"
        // flows through the Akka→MEL bridge to the capturing provider.
        var client = BuildClientViaUserDI();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var response = await client.SendAsync(new HttpRequestMessage(HttpMethod.Get, "/hello"), cts.Token);
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);

        await Task.Delay(TimeSpan.FromMilliseconds(200), cts.Token);

        var entries = _capture.Entries.ToList();
        var entry = entries.FirstOrDefault(e =>
            e.Level == LogLevel.Information &&
            e.Message.Contains("Creating stream instance", StringComparison.OrdinalIgnoreCase));

        Assert.NotNull(entry);
        // LoggerFactoryLogger routes all Akka actor messages under the "Akka.Actor.ActorSystem"
        // MEL category; the actor path (containing "stream-owner") is embedded in the message.
        Assert.Equal("Akka.Actor.ActorSystem", entry.CategoryName);
        Assert.Contains("stream-owner", entry.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(Timeout = 20000)]
    public async Task Akka_bridge_should_route_pipeline_materialized_message_to_MEL()
    {
        // Verifies that "Stream pipeline materialized successfully" (Debug) from
        // ClientStreamOwnerActor reaches the capturing provider.
        var client = BuildClientViaUserDI();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        await client.SendAsync(new HttpRequestMessage(HttpMethod.Get, "/hello"), cts.Token);
        await Task.Delay(TimeSpan.FromMilliseconds(200), cts.Token);

        var entries = _capture.Entries.ToList();
        Assert.Contains(entries, e =>
            e.Level == LogLevel.Debug &&
            e.Message.Contains("materialized", StringComparison.OrdinalIgnoreCase));
    }

    [Fact(Timeout = 20000)]
    public async Task TurboTrace_request_events_should_route_to_MEL_via_AddTurboLoggerTracing()
    {
        // Verifies that TracingBidiStage emits "Request started" / "Request completed"
        // to the TurboHttp.Trace.Request MEL category when AddTurboLoggerTracing() is called.
        var client = BuildClientViaUserDI(withTurboTrace: true);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var response = await client.SendAsync(new HttpRequestMessage(HttpMethod.Get, "/hello"), cts.Token);
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);

        await Task.Delay(TimeSpan.FromMilliseconds(200), cts.Token);

        var entries = _capture.Entries.ToList();

        Assert.Contains(entries, e =>
            e is { CategoryName: "TurboHttp.Trace.Request", Level: LogLevel.Information } &&
            e.Message.Contains("Request started:", StringComparison.OrdinalIgnoreCase));

        Assert.Contains(entries, e =>
            e is { CategoryName: "TurboHttp.Trace.Request", Level: LogLevel.Information } &&
            e.Message.Contains("Request completed:", StringComparison.OrdinalIgnoreCase));
    }

    [Fact(Timeout = 20000)]
    public async Task TurboTrace_connection_events_should_route_to_MEL_via_AddTurboLoggerTracing()
    {
        // Verifies that DirectConnectionFactory emits "Connection opened" to the
        // TurboHttp.Trace.Connection MEL category when AddTurboLoggerTracing() is called.
        var client = BuildClientViaUserDI(withTurboTrace: true);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        await client.SendAsync(new HttpRequestMessage(HttpMethod.Get, "/hello"), cts.Token);
        await Task.Delay(TimeSpan.FromMilliseconds(200), cts.Token);

        var entries = _capture.Entries.ToList();

        Assert.Contains(entries, e =>
            e is { CategoryName: "TurboHttp.Trace.Connection", Level: LogLevel.Information } &&
            e.Message.Contains("Connection opened:", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// A simple <see cref="ILoggerProvider"/> that captures all log entries for assertion.
    /// </summary>
    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public ConcurrentBag<LogEntry> Entries { get; } = [];

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, Entries);

        public void Dispose()
        {
        }
    }

    private sealed class CapturingLogger(string categoryName, ConcurrentBag<LogEntry> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            entries.Add(new LogEntry(categoryName, logLevel, formatter(state, exception)));
        }
    }

    public sealed record LogEntry(string CategoryName, LogLevel Level, string Message);
}