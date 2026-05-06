using System.Collections.Concurrent;
using System.Threading.Tasks.Sources;

namespace TurboHTTP;

internal sealed class PendingRequest : IValueTaskSource<HttpResponseMessage>
{
    private static readonly ConcurrentStack<PendingRequest> Pool = new();

    private ManualResetValueTaskSourceCore<HttpResponseMessage> _core = new() { RunContinuationsAsynchronously = true };

    private PendingRequest()
    {
    }

    public static PendingRequest Rent()
    {
        if (!Pool.TryPop(out var item)) return new PendingRequest();
        item._core.Reset();
        return item;
    }

    public static void Return(PendingRequest item) => Pool.Push(item);

    public short Version => _core.Version;

    public ValueTask<HttpResponseMessage> GetValueTask() => new(this, _core.Version);

    public bool TrySetResult(HttpResponseMessage response, short expectedVersion)
    {
        if (_core.Version != expectedVersion)
        {
            return false;
        }

        try
        {
            _core.SetResult(response);
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    public bool TrySetException(Exception exception)
    {
        try
        {
            _core.SetException(exception);
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    public bool TrySetCanceled(CancellationToken ct = default) => TrySetException(new OperationCanceledException(ct));

    public HttpResponseMessage GetResult(short token) => _core.GetResult(token);
    public ValueTaskSourceStatus GetStatus(short token) => _core.GetStatus(token);

    public void OnCompleted(Action<object?> continuation, object? state, short token,
        ValueTaskSourceOnCompletedFlags flags)
        => _core.OnCompleted(continuation, state, token, flags);
}