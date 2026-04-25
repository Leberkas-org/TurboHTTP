using System.Buffers;

namespace Servus.Akka.IO;

public static class ClientByteMover
{
    // Threshold below which consecutive small buffers are coalesced into a single write.
    // Reduces syscall overhead for HTTP/2 frame headers (9 bytes) and small DATA frames.
    private const int CoalesceThreshold = 32 * 1024;

    // Cached delegates — created once at class init, reused for every connection.
    // Avoids a delegate heap allocation on each MoveStreamToChannel call.
    private static readonly Func<int, NetworkBuffer> DefaultFactory = NetworkBuffer.Rent;
    internal static readonly Func<int, NetworkBuffer> Http3Factory = RoutedNetworkBuffer.Rent;

    public static async Task MoveStreamToChannel(
        ClientState state,
        Action onClose,
        CancellationToken ct,
        Func<int, NetworkBuffer>? bufferFactory = null)
    {
        bufferFactory ??= DefaultFactory;
        var abrupt = false;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var buffer = bufferFactory(128 * 1024);
                int bytesRead;
                try
                {
                    bytesRead = await state.Stream.ReadAsync(buffer.FullMemory, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    buffer.Dispose();
                    onClose();
                    return;
                }
                catch (Exception)
                {
                    buffer.Dispose();
                    abrupt = true;
                    onClose();
                    return;
                }

                if (bytesRead == 0)
                {
                    buffer.Dispose();
                    onClose();
                    return;
                }

                buffer.Length = bytesRead;
                if (!state.InboundWriter.TryWrite(buffer))
                {
                    buffer.Dispose();
                }
            }
        }
        finally
        {
            if (abrupt)
            {
                state.InboundWriter.TryComplete(new AbruptCloseException());
            }
            else
            {
                state.InboundWriter.TryComplete();
            }
        }
    }

    public static async Task MoveChannelToStream(ClientState state, Action onClose, CancellationToken ct)
    {
        // Coalesce buffer lives for the entire connection — rented lazily on first small write,
        // returned on exit. MemoryPool avoids a raw byte[] heap allocation (ArrayPool is banned).
        IMemoryOwner<byte>? coalesceOwner = null;

        try
        {
            while (!state.OutboundReader.Completion.IsCompleted)
            {
                try
                {
                    while (await state.OutboundReader.WaitToReadAsync(ct).ConfigureAwait(false))
                    {
                        var coalesceLen = 0;

                        while (state.OutboundReader.TryRead(out var buf))
                        {
                            try
                            {
                                var mem = buf.Memory;

                                if (mem.Length > CoalesceThreshold)
                                {
                                    if (coalesceLen > 0)
                                    {
                                        await state.Stream.WriteAsync(
                                            coalesceOwner!.Memory[..coalesceLen], ct).ConfigureAwait(false);
                                        coalesceLen = 0;
                                    }

                                    await state.Stream.WriteAsync(mem, ct).ConfigureAwait(false);
                                }
                                else
                                {
                                    coalesceOwner ??= MemoryPool<byte>.Shared.Rent(CoalesceThreshold);

                                    if (coalesceLen + mem.Length > coalesceOwner.Memory.Length)
                                    {
                                        await state.Stream.WriteAsync(
                                            coalesceOwner.Memory[..coalesceLen], ct).ConfigureAwait(false);
                                        coalesceLen = 0;
                                    }

                                    mem.CopyTo(coalesceOwner.Memory[coalesceLen..]);
                                    coalesceLen += mem.Length;
                                }
                            }
                            finally
                            {
                                buf.Dispose();
                            }
                        }

                        if (coalesceLen > 0)
                        {
                            await state.Stream.WriteAsync(
                                coalesceOwner!.Memory[..coalesceLen], ct).ConfigureAwait(false);
                        }

                        // No FlushAsync needed — Socket.NoDelay = true ensures data is sent immediately.
                        // For SslStream each WriteAsync already emits a self-contained TLS record.
                    }
                }
                catch (OperationCanceledException)
                {
                    onClose();
                    return;
                }
                catch (Exception)
                {
                    onClose();
                    return;
                }
            }
        }
        finally
        {
            coalesceOwner?.Dispose();
        }

        // Outbound channel drained normally — signal write-side FIN.
        // For QUIC request streams this calls QuicStream.CompleteWrites() so the server
        // sees end-of-request while the read side stays open for the response.
        state.OnWritesComplete?.Invoke();
    }
}
