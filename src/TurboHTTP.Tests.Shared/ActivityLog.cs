namespace TurboHTTP.Tests.Shared;

public abstract record Activity
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record WriteAttempt(int Index, byte[] Payload) : Activity;

public sealed record DisconnectEvent(string Reason) : Activity;

public sealed record ConnectionAbort : Activity;

public sealed record ResponseDelivered(int Index, int ByteCount) : Activity;

/// <summary>
/// Chronological log of typed transport activities for test assertions.
/// Not thread-safe; designed for single-threaded Akka stage execution.
/// </summary>
public sealed class ActivityLog
{
    private readonly List<Activity> _entries = [];

    public IReadOnlyList<Activity> Entries => _entries;

    public void Record(Activity activity) => _entries.Add(activity);

    public IEnumerable<T> OfType<T>() where T : Activity
        => _entries.OfType<T>();

    public void Clear() => _entries.Clear();
}
