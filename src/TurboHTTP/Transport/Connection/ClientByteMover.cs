using System.Buffers;
using TurboHTTP.Internal;

namespace TurboHTTP.Transport.Connection;

internal static class ClientByteMover
{
    /// <summary>
    /// Threshold below which consecutive small buffers are coalesced into a single write.
    /// Reduces syscall overhead for HTTP/2 frame headers (9 bytes) and small DATA frames.
    /// </summary>
    private const int CoalesceThreshold = 16 * 1024;
    /// <summary>
    /// Reads bytes directly from <paramref name="state"/>'s network stream into pooled buffers
    /// and writes them to the inbound channel. Eliminates the Pipe intermediary and the
    /// associated per-chunk copy.
    /// </summary>
    internal static async Task MoveStreamToChannel(ClientState state, Action onClose, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var buffer = NetworkBuffer.Rent(state.MaxFrameSize / 2);
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
                    state.CloseKind = TlsCloseKind.AbruptClose;
                    onClose();
                    return;
                }

                if (bytesRead == 0)
                {
                    buffer.Dispose();
                    state.CloseKind = TlsCloseKind.CleanClose;
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
            if (state.CloseKind == TlsCloseKind.AbruptClose)
            {
                state.InboundWriter.TryComplete(new AbruptCloseException());
            }
            else
            {
                state.InboundWriter.TryComplete();
            }
        }
    }

    internal static async Task MoveChannelToStream(ClientState state, Action onClose, CancellationToken ct)
    {
        // Coalesce buffer lives for the entire connection — avoids ArrayPool rent/return
        // per drain cycle. Rented lazily on first small write, returned on exit.
        var coalesceBuf = (byte[]?)null;

        try
        {
            while (!state.OutboundReader.Completion.IsCompleted)
            {
                try
                {
                    while (await state.OutboundReader.WaitToReadAsync(ct).ConfigureAwait(false))
                    {
                        // Drain all available buffers. When multiple small buffers are ready
                        // (common for HTTP/2 frame headers + small DATA frames), coalesce them
                        // into a single write to reduce syscall overhead.
                        var coalesceLen = 0;

                        while (state.OutboundReader.TryRead(out var buf))
                        {
                            try
                            {
                                var span = buf.Memory;

                                // If the buffer is large or coalescing would overflow, flush
                                // the coalesce buffer first, then write the large buffer directly.
                                if (span.Length > CoalesceThreshold)
                                {
                                    if (coalesceLen > 0)
                                    {
                                        await state.Stream.WriteAsync(
                                            coalesceBuf.AsMemory(0, coalesceLen), ct).ConfigureAwait(false);
                                        coalesceLen = 0;
                                    }

                                    await state.Stream.WriteAsync(span, ct).ConfigureAwait(false);
                                }
                                else
                                {
                                    // Small buffer — coalesce into a single write.
                                    coalesceBuf ??= ArrayPool<byte>.Shared.Rent(CoalesceThreshold);

                                    if (coalesceLen + span.Length > coalesceBuf.Length)
                                    {
                                        // Flush current batch before adding more.
                                        await state.Stream.WriteAsync(
                                            coalesceBuf.AsMemory(0, coalesceLen), ct).ConfigureAwait(false);
                                        coalesceLen = 0;
                                    }

                                    span.CopyTo(coalesceBuf.AsMemory(coalesceLen));
                                    coalesceLen += span.Length;
                                }
                            }
                            finally
                            {
                                buf.Dispose();
                            }
                        }

                        // Flush remaining coalesced data.
                        if (coalesceLen > 0)
                        {
                            await state.Stream.WriteAsync(
                                coalesceBuf.AsMemory(0, coalesceLen), ct).ConfigureAwait(false);
                        }

                        // No FlushAsync needed — Socket.NoDelay = true ensures data is
                        // sent immediately without Nagle buffering. For SslStream, each
                        // WriteAsync already emits a self-contained TLS record.
                    }
                }
                catch (OperationCanceledException)
                {
                    onClose();
                    return;
                }
                catch (Exception)
                {
                    state.CloseKind ??= TlsCloseKind.AbruptClose;
                    onClose();
                    return;
                }
            }
        }
        finally
        {
            if (coalesceBuf is not null)
            {
                ArrayPool<byte>.Shared.Return(coalesceBuf);
            }
        }
    }
}
