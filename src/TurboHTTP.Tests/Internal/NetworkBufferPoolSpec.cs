using TurboHTTP.Internal;

namespace TurboHTTP.Tests.Internal;

public sealed class NetworkBufferPoolSpec
{
    [Fact(Timeout = 5000)]
    public void Rent_should_return_usable_buffer_after_dispose_cycle()
    {
        var buf1 = NetworkBuffer.Rent(128);
        buf1.Length = 10;
        Assert.Equal(10, buf1.Length);
        buf1.Dispose();

        var buf2 = NetworkBuffer.Rent(128);
        Assert.True(buf2.Capacity >= 128);
        buf2.Dispose();
    }

    [Fact(Timeout = 5000)]
    public void Dispose_should_be_idempotent()
    {
        var buf = NetworkBuffer.Rent(64);
        buf.Dispose();
        buf.Dispose();
    }

    [Fact(Timeout = 10000)]
    public async Task Pool_should_survive_concurrent_rent_and_dispose()
    {
        const int threadCount = 8;
        const int iterationsPerThread = 500;

        using var barrier = new Barrier(threadCount);
        var exceptions = new System.Collections.Concurrent.ConcurrentBag<Exception>();

        var tasks = Enumerable.Range(0, threadCount).Select(_ => Task.Run(() =>
        {
            barrier.SignalAndWait();
            for (var i = 0; i < iterationsPerThread; i++)
            {
                try
                {
                    var buf = NetworkBuffer.Rent(64);
                    buf.Length = 1;
                    buf.Dispose();
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            }
        })).ToArray();

        await Task.WhenAll(tasks);

        Assert.Empty(exceptions);
    }

    [Fact(Timeout = 10000)]
    public void Pool_should_not_leak_when_disposed_from_multiple_threads_simultaneously()
    {
        const int count = 200;
        var buffers = new NetworkBuffer[count];
        for (var i = 0; i < count; i++)
        {
            buffers[i] = NetworkBuffer.Rent(64);
            buffers[i].Length = 1;
        }

        Parallel.ForEach(buffers, buf => buf.Dispose());

        var postBuf = NetworkBuffer.Rent(64);
        Assert.True(postBuf.Capacity >= 64);
        postBuf.Dispose();
    }
}
