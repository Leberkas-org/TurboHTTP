using System.Buffers;
using System.Net;
using System.Threading.Channels;
using Akka.Actor;
using Servus.Akka.IO;
using Servus.Akka.IO.Tcp;
using Servus.Akka.Tests.Utils;

namespace Servus.Akka.Tests.IO.Tcp;

public sealed class TcpTransportEventSpec
{
    [Fact(Timeout = 5000)]
    public void LeaseAcquired_should_preserve_lease()
    {
        var inbound = Channel.CreateUnbounded<NetworkBuffer>();
        var outbound = Channel.CreateUnbounded<NetworkBuffer>();
        var key = new RequestEndpoint
        {
            Scheme = "http",
            Host = "localhost",
            Port = 80,
            Version = HttpVersion.Version11
        };
        var handle = ConnectionHandle.CreateDirect(outbound.Writer, inbound.Reader, key);
        var state = new ClientState(Stream.Null, inbound, outbound);
        var lease = new ConnectionLease(handle, state);

        var evt = new LeaseAcquired(lease);

        Assert.Same(lease, evt.Lease);
    }

    [Fact(Timeout = 5000)]
    public void AcquisitionFailed_should_preserve_error()
    {
        var ex = new IOException("test");
        var evt = new AcquisitionFailed(ex);

        Assert.Same(ex, evt.Error);
    }

    [Fact(Timeout = 5000)]
    public void InboundBatch_should_preserve_fields()
    {
        var batch = ArrayPool<IInputItem>.Shared.Rent(8);
        var evt = new InboundBatch(batch, 3, 7);

        Assert.Same(batch, evt.Batch);
        Assert.Equal(3, evt.Count);
        Assert.Equal(7, evt.Gen);

        ArrayPool<IInputItem>.Shared.Return(batch);
    }

    [Fact(Timeout = 5000)]
    public void InboundComplete_should_preserve_fields()
    {
        var evt = new InboundComplete(TlsCloseKind.AbruptClose, 5);

        Assert.Equal(TlsCloseKind.AbruptClose, evt.CloseKind);
        Assert.Equal(5, evt.Gen);
    }

    [Fact(Timeout = 5000)]
    public void InboundPumpFailed_should_preserve_error()
    {
        var ex = new IOException("pump error");
        var evt = new InboundPumpFailed(ex);

        Assert.Same(ex, evt.Error);
    }

    [Fact(Timeout = 5000)]
    public void OutboundWriteDone_should_implement_interface()
    {
        ITcpTransportEvent evt = new OutboundWriteDone();

        Assert.IsType<OutboundWriteDone>(evt);
    }

    [Fact(Timeout = 5000)]
    public void OutboundWriteFailed_should_preserve_error()
    {
        var ex = new IOException("write error");
        var evt = new OutboundWriteFailed(ex);

        Assert.Same(ex, evt.Error);
    }

    [Fact(Timeout = 5000)]
    public void FlushNextCompleted_should_implement_interface()
    {
        ITcpTransportEvent evt = new FlushNextCompleted();

        Assert.IsType<FlushNextCompleted>(evt);
    }

    [Fact(Timeout = 5000)]
    public void InboundComplete_equality_should_compare_all_fields()
    {
        var a = new InboundComplete(TlsCloseKind.CleanClose, 1);
        var b = new InboundComplete(TlsCloseKind.CleanClose, 1);
        var c = new InboundComplete(TlsCloseKind.AbruptClose, 1);
        var d = new InboundComplete(TlsCloseKind.CleanClose, 2);

        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
        Assert.NotEqual(a, d);
    }

    [Fact(Timeout = 5000)]
    public void OutboundWriteDone_equality_should_match()
    {
        var a = new OutboundWriteDone();
        var b = new OutboundWriteDone();

        Assert.Equal(a, b);
    }

    [Fact(Timeout = 5000)]
    public void FlushNextCompleted_equality_should_match()
    {
        var a = new FlushNextCompleted();
        var b = new FlushNextCompleted();

        Assert.Equal(a, b);
    }
}
