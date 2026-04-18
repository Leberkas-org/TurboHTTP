namespace TurboHTTP.Protocol.Http3.Qpack;

/// <summary>
/// Represents a blocked stream waiting for dynamic table updates.
/// </summary>
internal sealed class BlockedStream
{
    /// <summary>The stream ID that is blocked.</summary>
    public int StreamId { get; }

    /// <summary>The Required Insert Count that must be reached to unblock.</summary>
    public int RequiredInsertCount { get; }

    /// <summary>The raw header block data to decode once unblocked.</summary>
    public ReadOnlyMemory<byte> Data { get; }

    public BlockedStream(int streamId, int requiredInsertCount, ReadOnlyMemory<byte> data)
    {
        StreamId = streamId;
        RequiredInsertCount = requiredInsertCount;
        Data = data;
    }
}