using System.Net;
using Servus.Akka.IO;
using Servus.Akka.IO.Quic;
using Servus.Akka.Tests.Utils;

#pragma warning disable CA1416

namespace Servus.Akka.Tests.IO.Quic;

public sealed class QuicTransportEventSpec
{
    [Fact(Timeout = 5000)]
    public void RequestLeaseAcquired_should_preserve_fields()
    {
        var lease = CreateTestConnectionLease();
        var evt = new RequestLeaseAcquired(lease, 42);

        Assert.Same(lease, evt.Lease);
        Assert.Equal(42, evt.StreamId);
    }

    [Fact(Timeout = 5000)]
    public void TypedLeaseAcquired_should_preserve_fields()
    {
        var lease = CreateTestConnectionLease();
        var evt = new TypedLeaseAcquired(lease, 0x00, 7);

        Assert.Same(lease, evt.Lease);
        Assert.Equal(0x00, evt.StreamTypeValue);
        Assert.Equal(7, evt.StreamId);
    }

    [Fact(Timeout = 5000)]
    public void AcquisitionFailed_should_preserve_error()
    {
        var ex = new IOException("test");
        var evt = new Servus.Akka.IO.Quic.AcquisitionFailed(ex);

        Assert.Same(ex, evt.Error);
    }

    [Fact(Timeout = 5000)]
    public void InboundData_should_preserve_fields()
    {
        var buf = NetworkBufferTestExtensions.FromArray([1, 2, 3]);
        var evt = new Servus.Akka.IO.Quic.InboundData(buf, 5);

        Assert.Same(buf, evt.Item);
        Assert.Equal(5, evt.Gen);

        buf.Dispose();
    }

    [Fact(Timeout = 5000)]
    public void InboundComplete_should_preserve_fields()
    {
        var evt = new Servus.Akka.IO.Quic.InboundComplete(QuicCloseKind.ConnectionFailure, 3, 42);

        Assert.Equal(QuicCloseKind.ConnectionFailure, evt.CloseKind);
        Assert.Equal(3, evt.Gen);
        Assert.Equal(42, evt.StreamId);
    }

    [Fact(Timeout = 5000)]
    public void InboundPumpFailed_should_preserve_fields()
    {
        var ex = new IOException("pump failed");
        var evt = new Servus.Akka.IO.Quic.InboundPumpFailed(ex, 99);

        Assert.Same(ex, evt.Error);
        Assert.Equal(99, evt.StreamId);
    }

    [Fact(Timeout = 5000)]
    public void OutboundWriteDone_should_implement_interface()
    {
        IQuicTransportEvent evt = new Servus.Akka.IO.Quic.OutboundWriteDone();

        Assert.IsType<Servus.Akka.IO.Quic.OutboundWriteDone>(evt);
    }

    [Fact(Timeout = 5000)]
    public void OutboundWriteFailed_should_preserve_error()
    {
        var ex = new IOException("write failed");
        var evt = new Servus.Akka.IO.Quic.OutboundWriteFailed(ex);

        Assert.Same(ex, evt.Error);
    }

    [Fact(Timeout = 5000)]
    public void EarlyDataRejected_should_preserve_buffer()
    {
        var buf = NetworkBufferTestExtensions.FromArray([1, 2, 3]);
        var evt = new EarlyDataRejected(buf);

        Assert.Same(buf, evt.Buffer);

        buf.Dispose();
    }

    [Fact(Timeout = 5000)]
    public void ConnectionMigrated_should_preserve_endpoints()
    {
        var oldEp = new IPEndPoint(IPAddress.Loopback, 1234);
        var newEp = new IPEndPoint(IPAddress.Loopback, 5678);
        var evt = new ConnectionMigrated(oldEp, newEp);

        Assert.Equal(oldEp, evt.OldLocalEndPoint);
        Assert.Equal(newEp, evt.NewLocalEndPoint);
    }

    [Fact(Timeout = 5000)]
    public void ConnectionMigrated_should_allow_null_endpoints()
    {
        var evt = new ConnectionMigrated(null, null);

        Assert.Null(evt.OldLocalEndPoint);
        Assert.Null(evt.NewLocalEndPoint);
    }

    [Fact(Timeout = 5000)]
    public void InboundComplete_equality_should_compare_all_fields()
    {
        var a = new Servus.Akka.IO.Quic.InboundComplete(QuicCloseKind.RequestStreamComplete, 1, 42);
        var b = new Servus.Akka.IO.Quic.InboundComplete(QuicCloseKind.RequestStreamComplete, 1, 42);
        var c = new Servus.Akka.IO.Quic.InboundComplete(QuicCloseKind.ConnectionFailure, 1, 42);

        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
    }

    private static ConnectionLease CreateTestConnectionLease()
    {
        var inbound = System.Threading.Channels.Channel.CreateUnbounded<NetworkBuffer>();
        var outbound = System.Threading.Channels.Channel.CreateUnbounded<NetworkBuffer>();
        var key = new RequestEndpoint
        {
            Scheme = "https",
            Host = "localhost",
            Port = 443,
            Version = new Version(3, 0)
        };
        var handle = ConnectionHandle.CreateDirect(outbound.Writer, inbound.Reader, key);
        var state = new ClientState(Stream.Null, inbound, outbound);
        return new ConnectionLease(handle, state);
    }
}
