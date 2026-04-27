using System.Threading.Channels;
using Akka.Actor;

namespace Servus.Akka.IO.Quic;

public sealed class QuicPumpManager
{
    private readonly IActorRef _self;
    private CancellationTokenSource? _pumpsCts;
    private CancellationTokenSource? _inboundAcceptCts;

    public QuicPumpManager(IActorRef self)
    {
        _self = self;
    }

    public bool IsAcceptLoopRunning => _inboundAcceptCts is { IsCancellationRequested: false };

    public void StartInboundPump(ConnectionHandle handle, long streamTypeValue,
        RequestEndpoint key, int connectionGen, long streamId)
    {
        _pumpsCts ??= new CancellationTokenSource();
        _ = PumpAsync(handle.InboundReader, key, streamTypeValue, _pumpsCts.Token, _self, connectionGen, streamId);
    }

    public void StartInboundAcceptLoop(QuicConnectionHandle connectionHandle)
    {
        _inboundAcceptCts?.Cancel();
        _inboundAcceptCts?.Dispose();
        _inboundAcceptCts = new CancellationTokenSource();

        _ = AcceptLoopAsync(connectionHandle, _self, _inboundAcceptCts.Token);
    }

    public void StopAll()
    {
        _inboundAcceptCts?.Cancel();
        _inboundAcceptCts?.Dispose();
        _inboundAcceptCts = null;

        _pumpsCts?.Cancel();
        _pumpsCts?.Dispose();
        _pumpsCts = null;
    }

    private static async Task AcceptLoopAsync(QuicConnectionHandle handle, IActorRef self,
        CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var inbound = await handle.AcceptInboundStreamAsLeaseAsync(ct).ConfigureAwait(false);

            if (ct.IsCancellationRequested)
            {
                inbound?.Lease.Dispose();
                return;
            }

            if (inbound is null)
            {
                continue;
            }

            self.Tell(new InboundStreamReady(inbound));
        }
    }

    private static async Task PumpAsync(
        ChannelReader<IoBuffer> reader,
        RequestEndpoint key,
        long streamTypeValue,
        CancellationToken ct,
        IActorRef self,
        int gen,
        long streamId)
    {
        var closeKind = QuicCloseKind.RequestStreamComplete;
        try
        {
            while (await reader.WaitToReadAsync(ct).ConfigureAwait(false))
            {
                while (reader.TryRead(out var chunk))
                {
                    var nb = RoutedNetworkBuffer.Wrap(chunk.Owner, chunk.Length);
                    nb.Key = key;
                    nb.StreamTypeValue = streamTypeValue;
                    nb.StreamId = streamId;

                    self.Tell(new InboundData(nb, gen));
                }
            }
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (AbruptCloseException)
        {
            closeKind = QuicCloseKind.ConnectionFailure;
        }
        catch (ChannelClosedException ex) when (ex.InnerException is AbruptCloseException)
        {
            closeKind = QuicCloseKind.ConnectionFailure;
        }
        catch (Exception ex)
        {
            self.Tell(new InboundPumpFailed(ex, streamId));
            return;
        }

        if (streamTypeValue < 0)
        {
            self.Tell(new InboundComplete(closeKind, gen, streamId));
        }
    }
}
