namespace TurboHTTP.Tests.Internal;

/// <summary>
/// Tests for <see cref="PendingRequest"/> — the pooled <see cref="IValueTaskSource{T}"/>
/// used by <see cref="TurboHttpClient"/>. Validates version-guarded completion,
/// pool reuse safety, and concurrent cancellation semantics.
/// </summary>
public sealed class PendingRequestSpec
{
    [Fact(Timeout = 5000)]
    public async Task TrySetResult_should_succeed_when_version_matches()
    {
        var pr = PendingRequest.Rent();
        var version = pr.Version;
        var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK);

        Assert.True(pr.TrySetResult(response, version));

        var result = await pr.GetValueTask();
        Assert.Same(response, result);

        PendingRequest.Return(pr);
    }

    [Fact(Timeout = 5000)]
    public void TrySetResult_should_fail_when_version_mismatches()
    {
        var pr = PendingRequest.Rent();
        var version = pr.Version;

        short staleVersion = (short)(version + 1);

        Assert.False(pr.TrySetResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK), staleVersion));

        PendingRequest.Return(pr);
    }

    [Fact(Timeout = 5000)]
    public void TrySetResult_should_fail_after_already_completed()
    {
        var pr = PendingRequest.Rent();
        var version = pr.Version;

        Assert.True(pr.TrySetResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK), version));
        Assert.False(pr.TrySetResult(new HttpResponseMessage(System.Net.HttpStatusCode.NotFound), version));

        PendingRequest.Return(pr);
    }

    [Fact(Timeout = 5000)]
    public async Task TrySetCanceled_should_complete_with_cancellation()
    {
        var pr = PendingRequest.Rent();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        Assert.True(pr.TrySetCanceled(cts.Token));

        await Assert.ThrowsAsync<OperationCanceledException>(async () => await pr.GetValueTask());

        PendingRequest.Return(pr);
    }

    [Fact(Timeout = 5000)]
    public async Task TrySetException_should_complete_with_exception()
    {
        var pr = PendingRequest.Rent();
        var exception = new InvalidOperationException("test error");

        Assert.True(pr.TrySetException(exception));

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await pr.GetValueTask());

        PendingRequest.Return(pr);
    }

    [Fact(Timeout = 5000)]
    public async Task Rent_should_reset_state_for_reused_instance()
    {
        var pr = PendingRequest.Rent();
        var v1 = pr.Version;
        pr.TrySetResult(new HttpResponseMessage(), v1);
        _ = await pr.GetValueTask();
        PendingRequest.Return(pr);

        var pr2 = PendingRequest.Rent();
        var v2 = pr2.Version;
        var response = new HttpResponseMessage(System.Net.HttpStatusCode.Accepted);

        Assert.True(pr2.TrySetResult(response, v2));
        Assert.Same(response, await pr2.GetValueTask());

        PendingRequest.Return(pr2);
    }

    [Fact(Timeout = 5000)]
    public async Task Version_guard_should_prevent_stale_pipeline_completion_after_reuse()
    {
        var pr = PendingRequest.Rent();
        var oldVersion = pr.Version;

        pr.TrySetResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK), oldVersion);
        _ = await pr.GetValueTask();
        PendingRequest.Return(pr);

        var pr2 = PendingRequest.Rent();
        var newVersion = pr2.Version;

        Assert.False(pr2.TrySetResult(new HttpResponseMessage(System.Net.HttpStatusCode.InternalServerError), oldVersion));

        var expected = new HttpResponseMessage(System.Net.HttpStatusCode.Created);
        Assert.True(pr2.TrySetResult(expected, newVersion));
        Assert.Same(expected, await pr2.GetValueTask());

        PendingRequest.Return(pr2);
    }

    [Fact(Timeout = 10000)]
    public async Task CancelPendingRequests_pattern_should_survive_concurrent_add_and_cancel()
    {
        var pendingTcs = new System.Collections.Concurrent.ConcurrentDictionary<PendingRequest, byte>();
        const int iterations = 500;
        var cancelCount = 0;
        var addCount = 0;

        using var barrier = new Barrier(2);

        var addTask = Task.Run(() =>
        {
            barrier.SignalAndWait();
            for (var i = 0; i < iterations; i++)
            {
                var pr = PendingRequest.Rent();
                pendingTcs.TryAdd(pr, 0);
                Interlocked.Increment(ref addCount);
            }
        }, TestContext.Current.CancellationToken);

        var cancelTask = Task.Run(() =>
        {
            barrier.SignalAndWait();
            for (var i = 0; i < iterations; i++)
            {
                foreach (var pending in pendingTcs.Keys)
                {
                    pending.TrySetCanceled();
                    if (pendingTcs.TryRemove(pending, out _))
                    {
                        Interlocked.Increment(ref cancelCount);
                        PendingRequest.Return(pending);
                    }
                }
            }
        }, TestContext.Current.CancellationToken);

        await Task.WhenAll(addTask, cancelTask);

        foreach (var pending in pendingTcs.Keys)
        {
            pending.TrySetCanceled();
            pendingTcs.TryRemove(pending, out _);
            PendingRequest.Return(pending);
        }

        Assert.True(addCount > 0);
    }
}
