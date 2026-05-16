using TurboHTTP.Internal;
using TurboHTTP.Protocol;

namespace TurboHTTP.Tests.Protocol;

public sealed class RequestFaultSpec
{
    [Fact(Timeout = 5000)]
    public async Task Fail_should_set_exception_on_pending_request()
    {
        // Arrange
        var request = new HttpRequestMessage();
        var pending = PendingRequest.Rent();
        var version = pending.Version;
        request.Options.Set(OptionsKey.Key, pending);
        request.Options.Set(OptionsKey.VersionKey, version);

        var exception = new InvalidOperationException("Test fault");
        var valueTask = new ValueTask<HttpResponseMessage>(pending, version);

        // Act
        request.Fail(exception);

        // Assert
        Assert.True(valueTask.IsFaulted);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await valueTask);

        // Cleanup
        PendingRequest.Return(pending);
    }

    [Fact(Timeout = 5000)]
    public void Fail_should_not_throw_when_request_has_no_pending()
    {
        // Arrange
        var request = new HttpRequestMessage();
        var exception = new InvalidOperationException("Test fault");

        // Act & Assert - should not throw
        request.Fail(exception);
    }

    [Fact(Timeout = 5000)]
    public async Task FailAll_should_fail_all_requests_in_collection()
    {
        // Arrange
        var requests = new List<HttpRequestMessage>(3);
        var pendings = new List<PendingRequest>(3);
        var valueTasks = new List<ValueTask<HttpResponseMessage>>(3);
        var exception = new InvalidOperationException("Test fault");

        for (var i = 0; i < 3; i++)
        {
            var request = new HttpRequestMessage();
            var pending = PendingRequest.Rent();
            var version = pending.Version;
            request.Options.Set(OptionsKey.Key, pending);
            request.Options.Set(OptionsKey.VersionKey, version);
            requests.Add(request);
            pendings.Add(pending);
            valueTasks.Add(new ValueTask<HttpResponseMessage>(pending, version));
        }

        // Act
        RequestFault.FailAll(requests, exception);

        // Assert
        for (var i = 0; i < valueTasks.Count; i++)
        {
            Assert.True(valueTasks[i].IsFaulted);
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await valueTasks[i]);
        }

        // Cleanup
        foreach (var pending in pendings)
        {
            PendingRequest.Return(pending);
        }
    }

    [Fact(Timeout = 5000)]
    public async Task FailAll_queue_should_fail_all_and_clear()
    {
        // Arrange
        var queue = new Queue<HttpRequestMessage>(2);
        var pendings = new List<PendingRequest>(2);
        var valueTasks = new List<ValueTask<HttpResponseMessage>>(2);
        var exception = new InvalidOperationException("Test fault");

        for (var i = 0; i < 2; i++)
        {
            var request = new HttpRequestMessage();
            var pending = PendingRequest.Rent();
            var version = pending.Version;
            request.Options.Set(OptionsKey.Key, pending);
            request.Options.Set(OptionsKey.VersionKey, version);
            queue.Enqueue(request);
            pendings.Add(pending);
            valueTasks.Add(new ValueTask<HttpResponseMessage>(pending, version));
        }

        Assert.Equal(2, queue.Count);

        // Act
        RequestFault.FailAll(queue, exception);

        // Assert
        Assert.Empty(queue);
        for (var i = 0; i < valueTasks.Count; i++)
        {
            Assert.True(valueTasks[i].IsFaulted);
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await valueTasks[i]);
        }

        // Cleanup
        foreach (var pending in pendings)
        {
            PendingRequest.Return(pending);
        }
    }
}