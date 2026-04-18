using System.Text;

namespace TurboHTTP.Protocol.Http2.Hpack;

/// <summary>
/// RFC 7541 §4.1 - Dynamic Table.
/// FIFO queue: newest entries at the front, oldest evicted on overflow.
/// Each entry costs: Name.Length + Value.Length + 32 bytes overhead (RFC 7541 §4.1).
/// Both the name byte length and total encoded size are computed once at insertion
/// and cached to avoid repeated <see cref="System.Text.Encoding.UTF8"/> GetByteCount
/// calls during eviction, header-list-size accounting, and name-reference lookups.
/// </summary>
internal sealed class HpackDynamicTable
{
    // RFC 7541 §4.2 - Default max size: 4096 bytes

    // Each slot stores the header, its name byte length, and total RFC 7541 §4.1 entry size.
    // NameByteLength is needed for literal header fields that reference an indexed name (§6.2.1/§6.2.2/§6.2.3).
    // EncodedSize (= nameBytes + valueBytes + 32) is used for eviction and header-list-size checks.
    private readonly List<(HpackHeader Header, int NameByteLength, int EncodedSize)> _entries = [];

    // RFC 7541 §4.2 - Default max size: 4096 bytes
    /// <summary>Currently configured maximum table size in bytes.</summary>
    public int MaxSize { get; private set; } = 4096;

    /// <summary>Currently occupied table size in bytes.</summary>
    public int CurrentSize { get; private set; }

    /// <summary>
    /// RFC 7541 §4.2 - Sets the maximum table size.
    /// Triggers eviction of oldest entries if the new limit is exceeded.
    /// </summary>
    public void SetMaxSize(int newMax)
    {
        if (newMax < 0)
        {
            throw new HpackException($"Invalid HPACK table size: {newMax}");
        }

        MaxSize = newMax;
        Evict();
    }

    /// <summary>
    /// RFC 7541 §4.4 - Adds a new entry to the front of the table.
    /// If the entry alone exceeds MaxSize, the entire table is cleared.
    /// Name byte length and total entry size are computed once here and cached.
    /// </summary>
    public void Add(string name, string value)
    {
        var nameByteLength = Encoding.UTF8.GetByteCount(name);
        var valueByteLength = Encoding.UTF8.GetByteCount(value);
        var entrySize = nameByteLength + valueByteLength + 32;

        // RFC 7541 §4.4: Entry larger than MaxSize -> evict everything
        if (entrySize > MaxSize)
        {
            Clear();
            return;
        }

        _entries.Add((new HpackHeader(name, value), nameByteLength, entrySize));
        CurrentSize += entrySize;
        Evict();
    }

    /// <summary>
    /// RFC 7541 §2.3.3 - Dynamic index is 1-based (relative to the table).
    /// Index 1 = most recently added entry.
    /// </summary>
    public HpackHeader? GetEntry(int dynamicIndex)
    {
        if (dynamicIndex <= 0 || dynamicIndex > _entries.Count)
        {
            return null;
        }

        // Newest entry is at the end of the list (index Count-1), dynamic index 1 = newest.
        return _entries[^dynamicIndex].Header;
    }

    /// <summary>
    /// Returns the header, its pre-computed name byte length, and total encoded entry size
    /// (name bytes + value bytes + 32) for the given 1-based dynamic index, or null if out of range.
    /// Used by the decoder to avoid re-computing byte counts for indexed references.
    /// </summary>
    public (HpackHeader Header, int NameByteLength, int EncodedSize)? GetEntryWithSizes(int dynamicIndex)
    {
        if (dynamicIndex <= 0 || dynamicIndex > _entries.Count)
        {
            return null;
        }

        var entry = _entries[^dynamicIndex];
        return (entry.Header, entry.NameByteLength, entry.EncodedSize);
    }

    /// <summary>Number of entries currently in the dynamic table.</summary>
    public int Count => _entries.Count;

    private void Evict()
    {
        while (CurrentSize > MaxSize && _entries.Count > 0)
        {
            // Oldest entry is at the front of the list (index 0).
            // Use cached EncodedSize — no GetByteCount call on eviction.
            var oldest = _entries[0];
            CurrentSize -= oldest.EncodedSize;
            _entries.RemoveAt(0);
        }
    }

    private void Clear()
    {
        _entries.Clear();
        CurrentSize = 0;
    }
}