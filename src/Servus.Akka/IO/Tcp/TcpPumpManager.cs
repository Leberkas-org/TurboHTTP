using System.Buffers;
using System.Threading.Channels;
using Akka.Actor;

namespace Servus.Akka.IO.Tcp;

/// <summary>
/// Manages the lifecycle of the TCP inbound pump — start, cancel, and the async read loop
/// that marshals batches of <see cref="NetworkBuffer"/> into StageActorRef messages.
/// Extracted from <see cref="TcpTransportStateMachine"/> for single-responsibility.
/// </summary>
internal sealed class TcpPumpManager
{
    private readonly IActorRef _self;
    private CancellationTokenSource? _pumpCts;

    public TcpPumpManager(IActorRef self)
    {
        _self = self;
    }

    public void StartInboundPump(ConnectionHandle handle, RequestEndpoint key, int gen)
    {
        StopInboundPump();

        _pumpCts = new CancellationTokenSource();
        _ = PumpAsync(handle.InboundReader, key, gen, _pumpCts.Token, _self);
    }

    public void StopInboundPump()
    {
        if (_pumpCts is null)
        {
            return;
        }

        _pumpCts.Cancel();
        _pumpCts.Dispose();
        _pumpCts = null;
    }

    private static async Task PumpAsync(
        ChannelReader<NetworkBuffer> reader,
        RequestEndpoint key,
        int gen,
        CancellationToken ct,
        IActorRef self)
    {
        var closeKind = TlsCloseKind.CleanClose;
        try
        {
            while (await reader.WaitToReadAsync(ct).ConfigureAwait(false))
            {
                IInputItem[]? batch = null;
                var count = 0;

                while (reader.TryRead(out var chunk))
                {
                    // Early exit when the connection generation changed — the actor thread
                    // always cancels the pump CTS after incrementing _connectionGen, so
                    // checking the token is sufficient. This avoids a cross-thread volatile
                    // read of _connectionGen from the pump's ThreadPool thread.
                    if (ct.IsCancellationRequested)
                    {
                        chunk.Dispose();
                        while (reader.TryRead(out var stale)) { stale.Dispose(); }
                        if (batch is not null) { ArrayPool<IInputItem>.Shared.Return(batch); }
                        return;
                    }

                    chunk.Key = key;
                    batch ??= ArrayPool<IInputItem>.Shared.Rent(32);

                    if (count == batch.Length)
                    {
                        self.Tell(new InboundBatch(batch, count, gen));
                        batch = ArrayPool<IInputItem>.Shared.Rent(count * 2);
                        count = 0;
                    }

                    batch[count++] = chunk;
                }

                if (count > 0)
                {
                    self.Tell(new InboundBatch(batch!, count, gen));
                }
                else if (batch is not null)
                {
                    ArrayPool<IInputItem>.Shared.Return(batch);
                }
            }
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (AbruptCloseException)
        {
            closeKind = TlsCloseKind.AbruptClose;
        }
        catch (ChannelClosedException ex) when (ex.InnerException is AbruptCloseException)
        {
            closeKind = TlsCloseKind.AbruptClose;
        }
        catch (Exception ex)
        {
            self.Tell(new InboundPumpFailed(ex));
            return;
        }

        self.Tell(new InboundComplete(closeKind, gen));
    }
}
