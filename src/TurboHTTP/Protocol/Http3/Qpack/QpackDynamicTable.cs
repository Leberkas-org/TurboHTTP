using System.Text;

namespace TurboHTTP.Protocol.Http3.Qpack;

/// <summary>
/// RFC 9204 §3.2 — QPACK Dynamic Table.
///
/// Unlike HPACK's relative indexing (newest = index 0), QPACK uses absolute indexing:
/// the first entry inserted has absolute index 0, the second has index 1, and so on.
/// This monotonically increasing scheme avoids head-of-line blocking because references
/// are unambiguous regardless of table mutations on other streams.
///
/// Each entry costs: Name.Length + Value.Length + 32 bytes overhead (RFC 9204 §3.2.1).
/// When capacity is exceeded, the oldest entries (lowest absolute indices) are evicted.
/// </summary>
internal sealed class QpackDynamicTable
{
    /// <summary>RFC 9204 §3.2.1 — Per-entry overhead in bytes.</summary>
    public const int EntryOverhead = 32;

    private readonly List<(int AbsoluteIndex, QpackEntry Entry, int Size)> _entries = [];

    /// <summary>
    /// Creates a new QPACK dynamic table with the specified maximum capacity in bytes.
    /// </summary>
    /// <param name="capacity">Maximum table size in bytes (0 = table disabled).</param>
    public QpackDynamicTable(int capacity = 0)
    {
        if (capacity < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity must be non-negative.");
        }

        Capacity = capacity;
    }

    /// <summary>Maximum table capacity in bytes.</summary>
    public int Capacity { get; private set; }

    /// <summary>Currently occupied table size in bytes.</summary>
    public int CurrentSize { get; private set; }

    /// <summary>Number of entries currently in the table.</summary>
    public int Count => _entries.Count;

    /// <summary>Direct indexed access to entries for iteration (0 = oldest, Count-1 = newest).</summary>
    internal (int AbsoluteIndex, QpackEntry Entry, int Size) this[int index] => _entries[index];

    /// <summary>
    /// RFC 9204 §3.2.4 — Total number of inserts into the dynamic table.
    /// This value only increases and is never reset, even when entries are evicted.
    /// Used by the encoder and decoder to synchronise state.
    /// </summary>
    public int InsertCount { get; private set; }

    /// <summary>
    /// RFC 9204 §3.2.3 — Sets the dynamic table capacity.
    /// Triggers eviction of oldest entries if the new capacity is exceeded.
    /// </summary>
    /// <param name="newCapacity">New maximum capacity in bytes (must be non-negative).</param>
    public void SetCapacity(int newCapacity)
    {
        if (newCapacity < 0)
        {
            throw new QpackException("RFC 9204 §3.2.3 violation: Dynamic table capacity must be non-negative.");
        }

        Capacity = newCapacity;
        Evict();
    }

    /// <summary>
    /// RFC 9204 §3.2.2 — Inserts a new entry into the dynamic table.
    /// The entry is assigned the next absolute index (<see cref="InsertCount"/> before increment).
    /// If the entry alone exceeds capacity, the table is cleared and the entry is not added.
    /// </summary>
    /// <returns>The absolute index assigned to the new entry, or -1 if it was too large.</returns>
    public int Insert(string name, string value)
    {
        var entrySize = CalculateEntrySize(name, value);

        // RFC 9204 §3.2.2: Entry larger than capacity → evict everything, do not insert
        if (entrySize > Capacity)
        {
            Clear();
            return -1;
        }

        // Evict oldest entries until there is room
        while (CurrentSize + entrySize > Capacity && _entries.Count > 0)
        {
            RemoveOldest();
        }

        var absoluteIndex = InsertCount;
        _entries.Add((absoluteIndex, new QpackEntry(name, value), entrySize));
        CurrentSize += entrySize;
        InsertCount++;

        return absoluteIndex;
    }

    /// <summary>
    /// RFC 9204 §3.2 — Looks up an entry by absolute index.
    /// Returns null if the entry has been evicted or the index is out of range.
    /// </summary>
    /// <param name="absoluteIndex">The absolute index (0-based, monotonically assigned).</param>
    public QpackEntry? GetEntry(int absoluteIndex)
    {
        if (absoluteIndex < 0 || absoluteIndex >= InsertCount || _entries.Count == 0)
        {
            return null;
        }

        // Entries are contiguous with monotonically increasing absolute indices.
        // Compute position directly: offset from the first entry's absolute index.
        var firstAbsoluteIndex = _entries[0].AbsoluteIndex;
        if (absoluteIndex < firstAbsoluteIndex)
        {
            return null; // Entry was evicted
        }

        var listIndex = absoluteIndex - firstAbsoluteIndex;
        if (listIndex >= _entries.Count)
        {
            return null;
        }

        return _entries[listIndex].Entry;
    }

    /// <summary>
    /// RFC 9204 §3.2.2 — Duplicates an existing entry by absolute index.
    /// The duplicate receives a new absolute index and is appended to the table.
    /// Returns -1 if the source entry has been evicted or the index is invalid.
    /// </summary>
    public int Duplicate(int absoluteIndex)
    {
        var entry = GetEntry(absoluteIndex);
        if (entry is null)
        {
            return -1;
        }

        return Insert(entry.Value.Name, entry.Value.Value);
    }

    /// <summary>
    /// Returns the lowest absolute index still present in the table,
    /// or -1 if the table is empty.
    /// </summary>
    public int DroppedCount => _entries.Count > 0 ? _entries[0].AbsoluteIndex : InsertCount;

    /// <summary>
    /// RFC 9204 §3.2.1 — Calculates the size of a header entry including the 32-byte overhead.
    /// </summary>
    public static int CalculateEntrySize(string name, string value)
        => Encoding.UTF8.GetByteCount(name) + Encoding.UTF8.GetByteCount(value) + EntryOverhead;

    private void Evict()
    {
        while (CurrentSize > Capacity && _entries.Count > 0)
        {
            RemoveOldest();
        }
    }

    private void RemoveOldest()
    {
        var oldest = _entries[0];
        CurrentSize -= oldest.Size;
        _entries.RemoveAt(0);
    }

    private void Clear()
    {
        _entries.Clear();
        CurrentSize = 0;
    }
}
