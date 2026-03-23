using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using TurboHttp.IntegrationTests.Shared;

namespace TurboHttp.IntegrationTests;

/// <summary>
/// Verifies that configuring the Akka → Microsoft.Extensions.Logging bridge
/// via <see cref="ILoggerFactory"/> in DI does not break TurboHttp client functionality.
/// </summary>
/// <remarks>
/// Note: <c>Akka.Logger.Extensions.Logging</c> 1.4.22 has a runtime incompatibility with
/// Akka.NET 1.5.62 (<c>LogMessage.get_Args()</c> signature mismatch). The bridge actor starts
/// but throws <c>MissingMethodException</c> when formatting messages. This does not break
/// client operation — Akka swallows the actor exception and the stream pipeline continues.
/// Once a compatible bridge package is available, the message-capture test should be enabled.
/// </remarks>
[Collection("Http1Integration")]
public sealed class LoggingBridgeTests : IAsyncLifetime
{
    private readonly KestrelFixture _fixture;
    private ClientHelper? _helper;
    private CapturingLoggerProvider? _loggerProvider;

    public LoggingBridgeTests(KestrelFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync()
    {
        _loggerProvider = new CapturingLoggerProvider();

        var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Debug);
            builder.AddProvider(_loggerProvider);
        });

        _helper = ClientHelper.CreateClient(
            _fixture.Port,
            new Version(1, 1),
            loggerFactory: loggerFactory);

        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        if (_helper is not null)
        {
            await _helper.DisposeAsync();
        }
    }

    [Fact(DisplayName = "TASK-009-003: HTTP request succeeds with ILoggerFactory configured in DI")]
    public async Task Http_Request_Succeeds_With_LoggerFactory_Configured()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var request = new HttpRequestMessage(HttpMethod.Get, "/hello");

        var response = await _helper!.Client.SendAsync(request, cts.Token);

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(cts.Token);
        Assert.Equal("Hello World", body);
    }

    [Fact(DisplayName = "TASK-009-003: Multiple requests succeed with logging bridge active")]
    public async Task Multiple_Requests_Succeed_With_Logging_Bridge()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        for (var i = 0; i < 3; i++)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "/hello");
            var response = await _helper!.Client.SendAsync(request, cts.Token);

            Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        }
    }

    [Fact(DisplayName = "TASK-009-003: Client without ILoggerFactory still works (fallback path)")]
    public async Task Client_Without_LoggerFactory_Still_Works()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        // Create a client without any ILoggerFactory — exercises the fallback path
        await using var helper = ClientHelper.CreateClient(
            _fixture.Port,
            new Version(1, 1));

        var request = new HttpRequestMessage(HttpMethod.Get, "/hello");
        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>
    /// A simple <see cref="ILoggerProvider"/> that captures all log entries for assertion.
    /// </summary>
    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public ConcurrentBag<LogEntry> Entries { get; } = new();

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, Entries);

        public void Dispose() { }
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
