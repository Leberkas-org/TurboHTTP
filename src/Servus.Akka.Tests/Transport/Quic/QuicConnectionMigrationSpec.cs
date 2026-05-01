using System.Net;
using Akka.Actor;
using Servus.Akka.Tests.Utils;
using Servus.Akka.Transport;
using Servus.Akka.Transport.Quic;

namespace Servus.Akka.Tests.Transport.Quic;

public sealed class QuicConnectionMigrationSpec
{

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9000-9")]
    public void QuicOptions_should_default_AllowConnectionMigration_to_true()
    {
        var options = new QuicTransportOptions { Host = "example.com", Port = 443 };
        Assert.True(options.AllowConnectionMigration);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9000-9")]
    public void QuicOptions_should_accept_AllowConnectionMigration_false()
    {
        var options = new QuicTransportOptions { Host = "example.com", Port = 443, AllowConnectionMigration = false };
        Assert.False(options.AllowConnectionMigration);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9000-9")]
    public void Dispatch_MigrationDetected_should_push_ConnectionMigrationDetected()
    {
        var ops = new StubOps();
        var sm = new QuicTransportStateMachine(ops, ActorRefs.Nobody, ActorRefs.Nobody);

        var oldEp = new IPEndPoint(IPAddress.Parse("10.0.0.1"), 12345);
        var newEp = new IPEndPoint(IPAddress.Parse("10.0.0.2"), 12345);

        sm.Dispatch(new MigrationDetected(oldEp, newEp));

        var migrationEvent = Assert.Single(ops.PushedInbound);
        var detected = Assert.IsType<ConnectionMigrationDetected>(migrationEvent);
        Assert.Equal(oldEp, detected.OldEndPoint);
        Assert.Equal(newEp, detected.NewEndPoint);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9000-9")]
    public void CheckForConnectionMigration_should_detect_endpoint_change()
    {
        var ops = new StubOps();
        var sm = new QuicTransportStateMachine(ops, ActorRefs.Nobody, ActorRefs.Nobody);

        var initialEp = new IPEndPoint(IPAddress.Parse("10.0.0.1"), 12345);
        var changedEp = new IPEndPoint(IPAddress.Parse("10.0.0.2"), 54321);
        var currentEp = initialEp;

        var handle = new QuicConnectionHandle(
            openStream: (_, _) => Task.FromResult((Stream: (Stream)new MemoryStream(), StreamId: 0L)),
            acceptInboundStream: async ct => { await Task.Delay(Timeout.Infinite, ct); return null; },
            getLocalEndPoint: () => currentEp,
            dispose: () => ValueTask.CompletedTask);

        var lease = new QuicConnectionLease(handle, 100);

        sm.HandlePush(new ConnectTransport(new QuicTransportOptions { Host = "example.com", Port = 443 }));
        sm.Dispatch(new ConnectionLeaseAcquired(lease));

        ops.PushedInbound.Clear();

        var buf1 = TransportBuffer.Rent(4);
        buf1.Length = 4;
        sm.Dispatch(new InboundData(buf1, 0, 2));

        var data1 = ops.PushedInbound.OfType<MultiplexedData>().FirstOrDefault();
        Assert.NotNull(data1);
        data1.Buffer.Dispose();

        ops.PushedInbound.Clear();
        currentEp = changedEp;

        var buf2 = TransportBuffer.Rent(4);
        buf2.Length = 4;
        sm.Dispatch(new InboundData(buf2, 0, 2));

        var data2 = ops.PushedInbound.OfType<MultiplexedData>().FirstOrDefault();
        Assert.NotNull(data2);
        data2.Buffer.Dispose();
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9000-9")]
    public void CheckForConnectionMigration_should_not_detect_when_endpoint_unchanged()
    {
        var ops = new StubOps();
        var sm = new QuicTransportStateMachine(ops, ActorRefs.Nobody, ActorRefs.Nobody);

        var stableEp = new IPEndPoint(IPAddress.Parse("10.0.0.1"), 12345);

        var handle = new QuicConnectionHandle(
            openStream: (_, _) => Task.FromResult((Stream: (Stream)new MemoryStream(), StreamId: 0L)),
            acceptInboundStream: async ct => { await Task.Delay(Timeout.Infinite, ct); return null; },
            getLocalEndPoint: () => stableEp,
            dispose: () => ValueTask.CompletedTask);

        var lease = new QuicConnectionLease(handle, 100);

        sm.HandlePush(new ConnectTransport(new QuicTransportOptions { Host = "example.com", Port = 443 }));
        sm.Dispatch(new ConnectionLeaseAcquired(lease));

        ops.PushedInbound.Clear();

        var buf1 = TransportBuffer.Rent(4);
        buf1.Length = 4;
        sm.Dispatch(new InboundData(buf1, 0, 2));

        Assert.DoesNotContain(ops.PushedInbound, i => i is ConnectionMigrationDetected);

        var data = ops.PushedInbound.OfType<MultiplexedData>().FirstOrDefault();
        Assert.NotNull(data);
        data.Buffer.Dispose();

        ops.PushedInbound.Clear();

        var buf2 = TransportBuffer.Rent(4);
        buf2.Length = 4;
        sm.Dispatch(new InboundData(buf2, 0, 2));

        Assert.DoesNotContain(ops.PushedInbound, i => i is ConnectionMigrationDetected);

        var data2 = ops.PushedInbound.OfType<MultiplexedData>().FirstOrDefault();
        Assert.NotNull(data2);
        data2.Buffer.Dispose();
    }
}