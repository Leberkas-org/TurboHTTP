namespace TurboHTTP.Protocol.Http3.Qpack;

/// <summary>
/// Result of a QPACK decode attempt that may be blocked.
/// </summary>
internal sealed class QpackDecodeResult
{
    private QpackDecodeResult(bool isBlocked, int requiredInsertCount, IReadOnlyList<(string Name, string Value)>? headers)
    {
        IsBlocked = isBlocked;
        RequiredInsertCount = requiredInsertCount;
        Headers = headers;
    }

    /// <summary>True if the stream is blocked waiting for dynamic table updates.</summary>
    public bool IsBlocked { get; }

    /// <summary>The Required Insert Count that must be reached before this block can be decoded.</summary>
    public int RequiredInsertCount { get; }

    /// <summary>The decoded headers, or null if the stream is blocked.</summary>
    public IReadOnlyList<(string Name, string Value)>? Headers { get; }

    /// <summary>Creates a successful decode result.</summary>
    public static QpackDecodeResult Success(IReadOnlyList<(string Name, string Value)> headers)
        => new(false, 0, headers);

    /// <summary>Creates a blocked decode result.</summary>
    public static QpackDecodeResult Blocked(int requiredInsertCount)
        => new(true, requiredInsertCount, null);
}