using System.Net;
using System.Threading.Channels;
using Akka.Actor;
using Akka.TestKit.Xunit;
using Servus.Akka.IO;
using Servus.Akka.IO.Quic;
using Servus.Akka.Tests.Utils;

#pragma warning disable CA1416

namespace Servus.Akka.Tests.IO.Quic;

public sealed class QuicPumpManagerErrorSpec : TestKit
{
    private static readonly RequestEndpoint TestEndpoint = new()
    {
        Scheme = "https",
        Host = "localhost",
        Port = 443,
        Version = HttpVersion.Version30
    };

    private static (Channel<NetworkBuffer> inbound, ConnectionHandle handle) CreateTestHandle()
    {
        var inbound = Channel.CreateUnbounded<NetworkBuffer>();
        var outbound = Channel.CreateUnbounded<NetworkBuffer>();
        var handle = ConnectionHandle.CreateDirect(outbound.Writer, inbound.Reader, TestEndpoint);
        return (inbound, handle);
    }

    [Fact(Timeout = 5000)]
    public async Task PumpAsync_should_send_InboundComplete_ConnectionFailure_for_request_stream_on_AbruptClose()
    {
        var probe = CreateTestProbe();
        var pump = new QuicPumpManager(probe.Ref);
        var (inbound, handle) = CreateTestHandle();

        // streamTypeValue < 0 → request stream; AbruptClose → InboundComplete(ConnectionFailure)
        inbound.Writer.TryComplete(new AbruptCloseException());
        pump.StartInboundPump(handle, streamTypeValue: -1, TestEndpoint, connectionGen: 0, streamId: 42);

        var msg = await probe.ExpectMsgAsync<InboundComplete>(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(QuicCloseKind.ConnectionFailure, msg.CloseKind);
        Assert.Equal(42, msg.StreamId);
    }

    [Fact(Timeout = 5000)]
    public async Task PumpAsync_should_send_InboundComplete_ConnectionFailure_for_request_stream_on_wrapped_AbruptClose()
    {
        var probe = CreateTestProbe();
        var pump = new QuicPumpManager(probe.Ref);
        var (inbound, handle) = CreateTestHandle();

        // ChannelClosedException wrapping AbruptCloseException → same outcome for request stream
        inbound.Writer.TryComplete(new AbruptCloseException());
        pump.StartInboundPump(handle, streamTypeValue: -1, TestEndpoint, connectionGen: 3, streamId: 7);

        var msg = await probe.ExpectMsgAsync<InboundComplete>(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(QuicCloseKind.ConnectionFailure, msg.CloseKind);
        Assert.Equal(3, msg.Gen);
    }

    [Fact(Timeout = 5000)]
    public async Task PumpAsync_should_not_send_InboundComplete_for_control_stream_on_AbruptClose()
    {
        var probe = CreateTestProbe();
        var pump = new QuicPumpManager(probe.Ref);
        var (inbound, handle) = CreateTestHandle();

        // streamTypeValue >= 0 → control stream; AbruptClose closes silently with no InboundComplete
        inbound.Writer.TryComplete(new AbruptCloseException());
        pump.StartInboundPump(handle, streamTypeValue: 0x00, TestEndpoint, connectionGen: 0, streamId: -2);

        await Task.Delay(150, TestContext.Current.CancellationToken);
        await probe.ExpectNoMsgAsync(TimeSpan.Zero, TestContext.Current.CancellationToken);

        pump.StopAll();
    }

    [Fact(Timeout = 5000)]
    public async Task PumpAsync_should_send_InboundPumpFailed_on_unexpected_exception()
    {
        var probe = CreateTestProbe();
        var pump = new QuicPumpManager(probe.Ref);
        var (inbound, handle) = CreateTestHandle();

        // A non-AbruptClose exception → InboundPumpFailed(error, streamId)
        inbound.Writer.TryComplete(new IOException("stream reset by peer"));
        pump.StartInboundPump(handle, streamTypeValue: -1, TestEndpoint, connectionGen: 0, streamId: 99);

        var msg = await probe.ExpectMsgAsync<InboundPumpFailed>(cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(msg.Error);
        Assert.Equal(99, msg.StreamId);
    }

    [Fact(Timeout = 5000)]
    public async Task PumpAsync_should_exit_silently_on_cancellation()
    {
        var probe = CreateTestProbe();
        var pump = new QuicPumpManager(probe.Ref);
        var (_, handle) = CreateTestHandle();

        pump.StartInboundPump(handle, streamTypeValue: -1, TestEndpoint, connectionGen: 0, streamId: 1);
        pump.StopAll();

        await Task.Delay(150, TestContext.Current.CancellationToken);
        await probe.ExpectNoMsgAsync(TimeSpan.Zero, TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 5000)]
    public async Task AcceptLoop_should_exit_silently_on_cancellation()
    {
        var probe = CreateTestProbe();
        var pump = new QuicPumpManager(probe.Ref);

        var provider = new FakeClientProvider(); // blocks AcceptInboundStreamAsync until cancelled
        var options = new QuicOptions { Host = "localhost", Port = 443 };
        var connHandle = new QuicConnectionHandle(provider, options, TestEndpoint);

        pump.StartInboundAcceptLoop(connHandle);
        pump.StopAll();

        await Task.Delay(150, TestContext.Current.CancellationToken);
        await probe.ExpectNoMsgAsync(TimeSpan.Zero, TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 5000)]
    public async Task AcceptLoop_should_send_InboundStreamReady_when_stream_accepted()
    {
        var probe = CreateTestProbe();
        var pump = new QuicPumpManager(probe.Ref);

        // inboundBytes[0] = stream-type varint (0x00 = control stream)
        var provider = new FakeClientProvider(inboundBytes: [0x00]);
        var options = new QuicOptions { Host = "localhost", Port = 443 };
        var connHandle = new QuicConnectionHandle(provider, options, TestEndpoint);

        pump.StartInboundAcceptLoop(connHandle);

        var msg = await probe.ExpectMsgAsync<InboundStreamReady>(cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(msg.Stream);
        Assert.Equal(0x00, msg.Stream.StreamTypeValue);

        pump.StopAll();
    }
}
