using System.Threading.Channels;

namespace TurboHTTP.Tests.Transport;

/// <summary>
/// Tests for the generation counter + CancellationToken pattern used by
/// <c>TcpTransportStateMachine</c>.
/// The actor thread owns <c>_connectionGen</c> (no cross-thread reads).
/// When a connection is torn down the actor increments the gen and cancels the
/// pump's <see cref="CancellationTokenSource"/>. The pump (ThreadPool) checks
/// <c>ct.IsCancellationRequested</c> to detect stale work, and the actor
/// uses <c>batch.Gen == _connectionGen</c> to discard stale batches.
/// </summary>
public sealed class GenerationCounterSpec
{
    [Fact(Timeout = 5000)]
    public void Stale_gen_should_cause_batch_to_be_discarded()
    {
        var currentGen = 5;
        var batchGen = 3;

        var processed = false;
        var discarded = false;

        if (batchGen == currentGen)
        {
            processed = true;
        }
        else
        {
            discarded = true;
        }

        Assert.False(processed);
        Assert.True(discarded);
    }

    [Fact(Timeout = 5000)]
    public void Current_gen_should_cause_batch_to_be_processed()
    {
        var currentGen = 5;
        var batchGen = 5;

        var processed = false;

        if (batchGen == currentGen)
        {
            processed = true;
        }

        Assert.True(processed);
    }

    [Fact(Timeout = 5000)]
    public async Task Pump_should_drain_and_exit_when_cts_canceled()
    {
        // Simulates the pump's inner loop: read items from a channel,
        // and when the CTS is canceled mid-read, dispose remaining items and exit.
        var channel = Channel.CreateUnbounded<StubItem>();
        var writer = channel.Writer;
        var reader = channel.Reader;

        var item1 = new StubItem();
        var item2 = new StubItem();
        var item3 = new StubItem();

        writer.TryWrite(item1);
        writer.TryWrite(item2);
        writer.TryWrite(item3);

        using var cts = new CancellationTokenSource();
        var processedCount = 0;
        var disposedCount = 0;

        // Simulate pump reading one item, then CTS gets canceled
        var pumpTask = Task.Run(() =>
        {
            while (reader.TryRead(out var item))
            {
                if (cts.Token.IsCancellationRequested)
                {
                    item.Dispose();
                    Interlocked.Increment(ref disposedCount);
                    while (reader.TryRead(out var stale))
                    {
                        stale.Dispose();
                        Interlocked.Increment(ref disposedCount);
                    }

                    return;
                }

                Interlocked.Increment(ref processedCount);

                // Actor cancels after first item is processed
                cts.Cancel();
            }
        });

        await pumpTask;

        Assert.Equal(1, processedCount);
        Assert.Equal(2, disposedCount);
        Assert.True(item1.IsAlive);
        Assert.True(item2.IsDisposed);
        Assert.True(item3.IsDisposed);
    }

    [Fact(Timeout = 5000)]
    public async Task Pump_should_process_all_items_when_not_canceled()
    {
        var channel = Channel.CreateUnbounded<StubItem>();
        var writer = channel.Writer;
        var reader = channel.Reader;

        var items = Enumerable.Range(0, 10).Select(_ => new StubItem()).ToArray();
        foreach (var item in items)
        {
            writer.TryWrite(item);
        }

        writer.Complete();

        using var cts = new CancellationTokenSource();
        var processedCount = 0;

        var pumpTask = Task.Run(async () =>
        {
            while (await reader.WaitToReadAsync(cts.Token))
            {
                while (reader.TryRead(out _))
                {
                    if (cts.Token.IsCancellationRequested)
                    {
                        return;
                    }

                    Interlocked.Increment(ref processedCount);
                }
            }
        });

        await pumpTask;

        Assert.Equal(10, processedCount);
        Assert.All(items, i => Assert.True(i.IsAlive));
    }

    [Fact(Timeout = 10000)]
    public async Task Gen_guard_should_discard_stale_batches_under_concurrent_gen_increments()
    {
        // Simulates the actor thread receiving batches while gen advances.
        // Batches stamped with an old gen must be discarded.
        var currentGen = 0;
        var processedCount = 0;
        var discardedCount = 0;
        const int batchCount = 1000;

        // Pre-generate batches: odd-indexed batches will be "stale"
        var batches = new (int Gen, int Index)[batchCount];
        for (var i = 0; i < batchCount; i++)
        {
            batches[i] = (Gen: i / 2, Index: i);
        }

        // Actor thread processes batches sequentially, incrementing gen periodically
        await Task.Run(() =>
        {
            for (var i = 0; i < batchCount; i++)
            {
                // Advance gen every 3 batches
                if (i % 3 == 0)
                {
                    currentGen++;
                }

                if (batches[i].Gen == currentGen)
                {
                    Interlocked.Increment(ref processedCount);
                }
                else
                {
                    Interlocked.Increment(ref discardedCount);
                }
            }
        });

        Assert.True(processedCount > 0, "At least some batches should match current gen");
        Assert.True(discardedCount > 0, "At least some batches should be stale");
        Assert.Equal(batchCount, processedCount + discardedCount);
    }

    private sealed class StubItem : IDisposable
    {
        public bool IsDisposed { get; private set; }
        public bool IsAlive => !IsDisposed;

        public void Dispose()
        {
            IsDisposed = true;
        }
    }
}
