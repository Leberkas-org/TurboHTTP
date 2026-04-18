using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Akka.Actor;
using Akka.Configuration;
using Akka.DependencyInjection;
using Akka.Hosting.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TurboHTTP.Diagnostics;

namespace TurboHTTP.AcceptanceTests.Diagnostics;

[CollectionDefinition("Logging", DisableParallelization = true)]
public sealed class LoggingCollectionDefinition;

[Collection("Logging")]
public sealed class LoggingBridgeSpec : IAsyncLifetime
{
    private static readonly Config LoggingHocon = ConfigurationFactory.ParseString("""
        akka.loggers = ["Akka.Hosting.Logging.LoggerFactoryLogger, Akka.Hosting"]
        akka.loglevel = DEBUG
        """);

    private Microsoft.Extensions.DependencyInjection.ServiceProvider? _provider;
    private CapturingLoggerProvider _capture = null!;
    private ITurboHttpClient? _client;
    private readonly CancellationTokenSource _serverCts = new();

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        TurboTrace.Disable();
        await _serverCts.CancelAsync();
        _serverCts.Dispose();

        if (_client is not null)
        {
            _client.Requests.TryComplete();
            try
            {
                await _client.Responses.Completion.WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch
            {
                // ignored
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

    private ITurboHttpClient BuildClientViaUserDI(int serverPort, bool withTurboTrace = false)
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
            var setup = BootstrapSetup.Create()
                .WithConfig(LoggingHocon)
                .And(diSetup)
                .And(new LoggerFactorySetup(loggerFactory));
            return ActorSystem.Create("turbohttp-bridge-test", setup);
        });

        // User step 2: register TurboHttp client
        services.AddTurboHttpClient(opts =>
        {
            opts.BaseAddress = new Uri($"http://127.0.0.1:{serverPort}");
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
        _client.BaseAddress = new Uri($"http://127.0.0.1:{serverPort}");
        _client.DefaultRequestVersion = new Version(1, 1);
        _client.Timeout = TimeSpan.FromMinutes(1);

        return _client;
    }

    private int StartFakeTcpServer()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var ct = _serverCts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    var client = await listener.AcceptTcpClientAsync(ct);
                    _ = ServeConnectionAsync(client, ct);
                }
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                listener.Stop();
            }
        }, CancellationToken.None);

        return port;
    }

    private static async Task ServeConnectionAsync(TcpClient client, CancellationToken ct)
    {
        try
        {
            using var _ = client;
            var stream = client.GetStream();
            var buffer = new byte[8192];
            var total = 0;

            while (total < buffer.Length)
            {
                var n = await stream.ReadAsync(buffer.AsMemory(total), ct);
                if (n == 0)
                {
                    return;
                }

                total += n;
                if (Encoding.ASCII.GetString(buffer, 0, total).Contains("\r\n\r\n"))
                {
                    break;
                }
            }

            var response = "HTTP/1.1 200 OK\r\nContent-Length: 5\r\nConnection: close\r\n\r\nHello"u8;
            await stream.WriteAsync(response.ToArray(), ct);
            await stream.FlushAsync(ct);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception)
        {
            // ignored
        }
    }

    [Fact(Timeout = 20000)]
    public async Task Akka_bridge_should_route_pipeline_materialized_message_to_MEL()
    {
        // Verifies that "Stream pipeline materialized successfully" (Debug) from
        // ClientStreamOwnerActor reaches the capturing provider.
        var port = StartFakeTcpServer();
        var client = BuildClientViaUserDI(port);
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
        var port = StartFakeTcpServer();
        var client = BuildClientViaUserDI(port, withTurboTrace: true);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var response = await client.SendAsync(new HttpRequestMessage(HttpMethod.Get, "/hello"), cts.Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await Task.Delay(TimeSpan.FromMilliseconds(200), cts.Token);

        var entries = _capture.Entries.ToList();

        Assert.Contains(entries, e =>
            e is { CategoryName: "TurboHTTP.Trace.Request", Level: LogLevel.Information } &&
            e.Message.Contains("Request started:", StringComparison.OrdinalIgnoreCase));

        Assert.Contains(entries, e =>
            e is { CategoryName: "TurboHTTP.Trace.Request", Level: LogLevel.Information } &&
            e.Message.Contains("Request completed:", StringComparison.OrdinalIgnoreCase));
    }

    [Fact(Timeout = 20000)]
    public async Task TurboTrace_connection_events_should_route_to_MEL_via_AddTurboLoggerTracing()
    {
        // Verifies that DirectConnectionFactory emits "Connection opened" to the
        // TurboHttp.Trace.Connection MEL category when AddTurboLoggerTracing() is called.
        var port = StartFakeTcpServer();
        var client = BuildClientViaUserDI(port, withTurboTrace: true);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        await client.SendAsync(new HttpRequestMessage(HttpMethod.Get, "/hello"), cts.Token);
        await Task.Delay(TimeSpan.FromMilliseconds(200), cts.Token);

        var entries = _capture.Entries.ToList();

        Assert.Contains(entries, e =>
            e is { CategoryName: "TurboHTTP.Trace.Connection", Level: LogLevel.Information } &&
            e.Message.Contains("Connection opened:", StringComparison.OrdinalIgnoreCase));
    }

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

    private sealed record LogEntry(string CategoryName, LogLevel Level, string Message);
}