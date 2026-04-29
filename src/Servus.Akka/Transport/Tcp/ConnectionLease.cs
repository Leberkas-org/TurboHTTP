namespace Servus.Akka.Transport.Tcp;

public sealed class ConnectionLease : IDisposable
{
    private readonly CancellationTokenSource _cts;
    private readonly ClientState _state;
    private readonly long _createdTicks = Environment.TickCount64;
    private bool _alive = true;

    internal ConnectionLease(ConnectionHandle handle, ClientState state, CancellationTokenSource cts)
    {
        Handle = handle;
        _state = state;
        _cts = cts;
    }

    public ConnectionHandle Handle { get; }

    internal ClientState State => _state;

    public bool IsAlive() => _alive;

    public bool IsExpired(TimeSpan maxLifetime)
    {
        if (maxLifetime == Timeout.InfiniteTimeSpan)
        {
            return false;
        }

        return Environment.TickCount64 - _createdTicks > (long)maxLifetime.TotalMilliseconds;
    }

    public void Dispose()
    {
        if (!_alive)
        {
            return;
        }

        _alive = false;
        _cts.Cancel();
        _cts.Dispose();
        _state.Dispose();
    }
}
