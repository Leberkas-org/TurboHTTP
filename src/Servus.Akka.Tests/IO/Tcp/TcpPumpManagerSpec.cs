using System.Buffers;
using System.Net;
using System.Threading.Channels;
using Akka.Actor;
using Akka.TestKit.Xunit;
using Servus.Akka.IO;
using Servus.Akka.IO.Tcp;

namespace Servus.Akka.Tests.IO.Tcp;

public sealed class TcpPumpManagerSpec : TestKit
{
    private static readonly RequestEndpoint TestEndpoint = new()
    {
        Scheme = "http",
        Host = "localhost",
        Port = 8080,
        Version = HttpVersion.Version11
    };

    private static (Channel<NetworkBuffer> inbound, ConnectionHandle handle) CreateTestHandle()
    {
        var inbound = Channel.CreateUnbounded<NetworkBuffer>();
        var outbound = Channel.CreateUnbounded<NetworkBuffer>();
        var handle = ConnectionHandle.CreateDirect(outbound.Writer, inbound.Reader, TestEndpoint);
        return (inbound, handle);
    }

    [Fact(Timeout = 5000)]
    public async Task PumpAsync_should_send_InboundComplete_CleanClose_when_channel_completes_normally()
    {
        var probe = CreateTestProbe();
        var pump = new TcpPumpManager(probe.Ref);
        var (inbound, handle) = CreateTestHandle();

        inbound.Writer.TryComplete();
        pump.StartInboundPump(handle, TestEndpoint, gen: 1);

        var msg = await probe.ExpectMsgAsync<InboundComplete>(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(TlsCloseKind.CleanClose, msg.CloseKind);
        Assert.Equal(1, msg.Gen);
    }

    [Fact(Timeout = 5000)]
    public async Task PumpAsync_should_send_InboundComplete_AbruptClose_when_channel_closed_with_inner_AbruptCloseException()
    {
        var probe = CreateTestProbe();
        var pump = new TcpPumpManager(probe.Ref);
        var (inbound, handle) = CreateTestHandle();

        // TryComplete(AbruptCloseException) → WaitToReadAsync throws ChannelClosedException(AbruptCloseException)
        inbound.Writer.TryComplete(new AbruptCloseException());
        pump.StartInboundPump(handle, TestEndpoint, gen: 2);

        var msg = await probe.ExpectMsgAsync<InboundComplete>(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(TlsCloseKind.AbruptClose, msg.CloseKind);
        Assert.Equal(2, msg.Gen);
    }

    [Fact(Timeout = 5000)]
    public async Task PumpAsync_should_send_InboundPumpFailed_on_unexpected_exception()
    {
        var probe = CreateTestProbe();
        var pump = new TcpPumpManager(probe.Ref);
        var (inbound, handle) = CreateTestHandle();

        // Non-AbruptClose exception → ChannelClosedException(IOException) → caught by catch(Exception)
        inbound.Writer.TryComplete(new IOException("unexpected I/O error"));
        pump.StartInboundPump(handle, TestEndpoint, gen: 0);

        var msg = await probe.ExpectMsgAsync<InboundPumpFailed>(cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(msg.Error);
    }

    [Fact(Timeout = 5000)]
    public async Task StopInboundPump_should_cancel_pump_and_send_no_messages()
    {
        var probe = CreateTestProbe();
        var pump = new TcpPumpManager(probe.Ref);
        var (_, handle) = CreateTestHandle();

        pump.StartInboundPump(handle, TestEndpoint, gen: 0);
        pump.StopInboundPump();

        // Allow some time for any stray messages to arrive
        await Task.Delay(150, TestContext.Current.CancellationToken);
        await probe.ExpectNoMsgAsync(TimeSpan.Zero, TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 5000)]
    public async Task PumpAsync_should_flush_and_grow_batch_when_full()
    {
        var probe = CreateTestProbe();
        var pump = new TcpPumpManager(probe.Ref);
        var (inbound, handle) = CreateTestHandle();

        // Detect the actual ArrayPool bucket size for Rent(8) at runtime (may be 8, 16, etc.)
        var sampleBatch = ArrayPool<IInputItem>.Shared.Rent(8);
        var initialBatchSize = sampleBatch.Length;
        ArrayPool<IInputItem>.Shared.Return(sampleBatch);

        // Write initialBatchSize+1 items: the first initialBatchSize trigger a full-batch flush,
        // then item initialBatchSize+1 lands in the grown batch.
        // Expected: InboundBatch(initialBatchSize) → InboundBatch(1) → InboundComplete(CleanClose)
        for (var i = 0; i < initialBatchSize + 1; i++)
        {
            await inbound.Writer.WriteAsync(NetworkBuffer.Rent(1), TestContext.Current.CancellationToken);
        }

        inbound.Writer.TryComplete();
        pump.StartInboundPump(handle, TestEndpoint, gen: 0);

        var batch1 = await probe.ExpectMsgAsync<InboundBatch>(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(initialBatchSize, batch1.Count);

        var batch2 = await probe.ExpectMsgAsync<InboundBatch>(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(1, batch2.Count);

        var complete = await probe.ExpectMsgAsync<InboundComplete>(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(TlsCloseKind.CleanClose, complete.CloseKind);
    }

    [Fact(Timeout = 5000)]
    public async Task StartInboundPump_should_cancel_previous_pump_when_called_again()
    {
        var probe = CreateTestProbe();
        var pump = new TcpPumpManager(probe.Ref);

        var (inbound1, handle1) = CreateTestHandle();
        var (inbound2, handle2) = CreateTestHandle();

        // Start first pump — channel stays open
        pump.StartInboundPump(handle1, TestEndpoint, gen: 1);

        // Start second pump — cancels the first
        inbound2.Writer.TryComplete();
        pump.StartInboundPump(handle2, TestEndpoint, gen: 2);

        // Only messages from pump2 expected; pump1 was cancelled
        var complete = await probe.ExpectMsgAsync<InboundComplete>(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(2, complete.Gen);

        // Write to the cancelled pump1 channel — should produce no further messages
        await inbound1.Writer.WriteAsync(NetworkBuffer.Rent(1), TestContext.Current.CancellationToken);
        await Task.Delay(100, TestContext.Current.CancellationToken);
        await probe.ExpectNoMsgAsync(TimeSpan.Zero, TestContext.Current.CancellationToken);
    }
}
