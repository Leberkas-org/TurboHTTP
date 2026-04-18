using System.Net;
using Akka.Actor;
using Akka.Event;
using TurboHTTP.Internal;
using TurboHTTP.Transport.Connection;
using TurboHTTP.Transport.Quic;
using TurboHTTP.Transport.Tcp;

namespace TurboHTTP.Tests.Http3.Connection;

public sealed class QuicConnectionMigrationSpec
{
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9000-9")]
    public void Http3Options_should_default_AllowConnectionMigration_to_true()
    {
        var options = new Http3Options();
        Assert.True(options.AllowConnectionMigration);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9000-9")]
    public void Http3Options_should_accept_AllowConnectionMigration_false()
    {
        var options = new Http3Options { AllowConnectionMigration = false };
        Assert.False(options.AllowConnectionMigration);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9000-9")]
    public void Http3EngineOptions_should_default_AllowConnectionMigration_to_true()
    {
        var options = new Http3Options().ToEngineOptions();
        Assert.True(options.AllowConnectionMigration);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9000-9")]
    public void Http3EngineOptions_should_accept_AllowConnectionMigration_false()
    {
        var options = new Http3Options { AllowConnectionMigration = false }.ToEngineOptions();
        Assert.False(options.AllowConnectionMigration);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9000-9")]
    public void QuicOptions_should_default_AllowConnectionMigration_to_true()
    {
        var options = new QuicOptions { Host = "example.com", Port = 443 };
        Assert.True(options.AllowConnectionMigration);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9000-9")]
    public void QuicOptions_should_accept_AllowConnectionMigration_false()
    {
        var options = new QuicOptions { Host = "example.com", Port = 443, AllowConnectionMigration = false };
        Assert.False(options.AllowConnectionMigration);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9000-9")]
    public void Migration_allowed_should_continue_transparently_when_address_changes()
    {
        // Arrange
        var ops = new StubTransportOperations();
        var sm = new QuicTransportStateMachine(ops, Nobody.Instance, Nobody.Instance,
            new TurboClientOptions(), allowConnectionMigration: true);

        var oldEndPoint = new IPEndPoint(IPAddress.Parse("192.168.1.10"), 12345);
        var newEndPoint = new IPEndPoint(IPAddress.Parse("10.0.0.5"), 54321);

        // Act — dispatch migration event
        sm.Dispatch(new ConnectionMigrated(oldEndPoint, newEndPoint));

        // Assert — no close signal emitted (connection continues transparently)
        Assert.Empty(ops.PushedOutputs);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9000-9")]
    public void Migration_disallowed_should_trigger_reconnect_when_address_changes()
    {
        // Arrange
        var ops = new StubTransportOperations();
        var sm = new QuicTransportStateMachine(ops, Nobody.Instance, Nobody.Instance,
            new TurboClientOptions(), allowConnectionMigration: false);

        var oldEndPoint = new IPEndPoint(IPAddress.Parse("192.168.1.10"), 12345);
        var newEndPoint = new IPEndPoint(IPAddress.Parse("10.0.0.5"), 54321);

        // Act — dispatch migration event
        sm.Dispatch(new ConnectionMigrated(oldEndPoint, newEndPoint));

        // Assert — close signal emitted with MigrationDisallowed (triggers reconnect via upstream)
        var closeSignal = Assert.Single(ops.PushedOutputs);
        var closeItem = Assert.IsType<QuicCloseItem>(closeSignal);
        Assert.Equal(QuicCloseKind.MigrationDisallowed, closeItem.Kind);
    }

    private sealed class StubTransportOperations : ITransportOperations
    {
        public List<IInputItem> PushedOutputs { get; } = [];
        public int PullCount { get; private set; }

        public void OnPushOutput(IInputItem item) => PushedOutputs.Add(item);
        public void OnSignalPullInput() => PullCount++;

        public void OnCompleteStage()
        {
        }

        public void OnScheduleTimer(string key, TimeSpan delay)
        {
        }

        public void OnCancelTimer(string key)
        {
        }

        public ILoggingAdapter Log { get; } = NoLogger.Instance;
    }
}