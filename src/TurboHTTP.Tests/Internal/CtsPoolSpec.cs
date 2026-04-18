using System.Collections.Concurrent;

namespace TurboHTTP.Tests.Internal;

public sealed class CtsPoolSpec
{
    private const int PoolCap = 64;

    [Fact(Timeout = 5000)]
    public void Pool_should_reuse_cts_after_tryReset()
    {
        var pool = new ConcurrentStack<CancellationTokenSource>();
        var poolCount = 0;

        var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromHours(1));

        Assert.True(cts.TryReset());
        if (Interlocked.Increment(ref poolCount) <= PoolCap)
        {
            pool.Push(cts);
        }
        else
        {
            Interlocked.Decrement(ref poolCount);
            cts.Dispose();
        }

        Assert.Equal(1, poolCount);
        Assert.True(pool.TryPop(out var reused));
        Interlocked.Decrement(ref poolCount);
        Assert.Same(cts, reused);
        Assert.Equal(0, poolCount);

        reused.Dispose();
    }

    [Fact(Timeout = 5000)]
    public void Pool_should_not_reuse_canceled_linked_cts()
    {
        // Linked CTS whose parent was canceled cannot be reset — this is why
        // the production code always disposes linked CTS (never returns to pool).
        using var parent = new CancellationTokenSource();
        parent.Cancel();
        var linked = CancellationTokenSource.CreateLinkedTokenSource(parent.Token);

        Assert.True(linked.IsCancellationRequested);
        Assert.False(linked.TryReset());

        linked.Dispose();
    }

    [Fact(Timeout = 5000)]
    public void Pool_should_not_reuse_canceled_cts()
    {
        var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.False(cts.TryReset());

        cts.Dispose();
    }

    [Fact(Timeout = 10000)]
    public async Task Pool_counter_should_stay_bounded_under_concurrent_returns()
    {
        var pool = new ConcurrentStack<CancellationTokenSource>();
        var poolCount = 0;
        const int threadCount = 16;
        const int iterationsPerThread = 200;

        using var barrier = new Barrier(threadCount);

        var tasks = Enumerable.Range(0, threadCount).Select(_ => Task.Run(() =>
        {
            barrier.SignalAndWait();
            for (var i = 0; i < iterationsPerThread; i++)
            {
                var cts = new CancellationTokenSource();
                cts.CancelAfter(TimeSpan.FromHours(1));

                if (cts.TryReset())
                {
                    if (Interlocked.Increment(ref poolCount) <= PoolCap)
                    {
                        pool.Push(cts);
                    }
                    else
                    {
                        Interlocked.Decrement(ref poolCount);
                        cts.Dispose();
                    }
                }
                else
                {
                    cts.Dispose();
                }
            }
        })).ToArray();

        await Task.WhenAll(tasks);

        Assert.True(poolCount <= PoolCap,
            $"Pool counter {poolCount} exceeded cap {PoolCap}");
        Assert.True(pool.Count <= PoolCap,
            $"Pool size {pool.Count} exceeded cap {PoolCap}");

        while (pool.TryPop(out var cts))
        {
            cts.Dispose();
        }
    }

    [Fact(Timeout = 10000)]
    public async Task Pool_should_survive_concurrent_rent_and_return()
    {
        var pool = new ConcurrentStack<CancellationTokenSource>();
        var poolCount = 0;
        const int threadCount = 8;
        const int iterationsPerThread = 300;

        for (var i = 0; i < PoolCap / 2; i++)
        {
            pool.Push(new CancellationTokenSource());
            Interlocked.Increment(ref poolCount);
        }

        using var barrier = new Barrier(threadCount);

        var tasks = Enumerable.Range(0, threadCount).Select(_ => Task.Run(() =>
        {
            barrier.SignalAndWait();
            for (var i = 0; i < iterationsPerThread; i++)
            {
                CancellationTokenSource cts;

                if (pool.TryPop(out var pooled))
                {
                    Interlocked.Decrement(ref poolCount);
                    cts = pooled;
                }
                else
                {
                    cts = new CancellationTokenSource();
                }

                cts.CancelAfter(TimeSpan.FromHours(1));
                Thread.SpinWait(10);

                if (cts.TryReset())
                {
                    if (Interlocked.Increment(ref poolCount) <= PoolCap)
                    {
                        pool.Push(cts);
                    }
                    else
                    {
                        Interlocked.Decrement(ref poolCount);
                        cts.Dispose();
                    }
                }
                else
                {
                    cts.Dispose();
                }
            }
        })).ToArray();

        await Task.WhenAll(tasks);

        Assert.True(poolCount <= PoolCap,
            $"Pool counter {poolCount} exceeded cap {PoolCap}");

        while (pool.TryPop(out var cts))
        {
            cts.Dispose();
        }
    }
}
