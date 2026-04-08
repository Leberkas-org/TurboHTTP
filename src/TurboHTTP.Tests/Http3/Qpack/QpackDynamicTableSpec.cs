using TurboHTTP.Protocol.Http3.Qpack;

namespace TurboHTTP.Tests.Http3.Qpack;

/// <summary>
/// Tests for QPACK dynamic table per RFC 9204 §3.2.
/// Covers absolute indexing, insert count tracking, capacity management, and eviction.
/// </summary>
public sealed class QpackDynamicTableSpec
{
    /// RFC 9204 §3.2 — Absolute indexing starts at 0 and increases monotonically
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-3.2")]
    public void Should_AssignAbsoluteIndices_When_Inserting()
    {
        var table = new QpackDynamicTable(4096);

        var idx0 = table.Insert(":method", "GET");
        var idx1 = table.Insert(":path", "/");
        var idx2 = table.Insert("content-type", "text/html");

        Assert.Equal(0, idx0);
        Assert.Equal(1, idx1);
        Assert.Equal(2, idx2);
        Assert.Equal(3, table.InsertCount);
    }

    /// RFC 9204 §3.2 — Entries retrievable by absolute index
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-3.2")]
    public void Should_ReturnEntry_When_LookedUpByAbsoluteIndex()
    {
        var table = new QpackDynamicTable(4096);

        table.Insert(":method", "GET");
        table.Insert(":path", "/");

        var entry0 = table.GetEntry(0);
        var entry1 = table.GetEntry(1);

        Assert.NotNull(entry0);
        Assert.Equal(":method", entry0.Value.Name);
        Assert.Equal("GET", entry0.Value.Value);

        Assert.NotNull(entry1);
        Assert.Equal(":path", entry1.Value.Name);
        Assert.Equal("/", entry1.Value.Value);
    }

    /// RFC 9204 §3.2.4 — Insert count only increases, never resets on eviction
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-3.2")]
    public void Should_MaintainInsertCount_When_EntriesEvicted()
    {
        // Small capacity: fits ~1 entry (":method" + "GET" = 7+3+32 = 42 bytes, so 80 fits ~1)
        var table = new QpackDynamicTable(80);

        table.Insert(":method", "GET");   // index 0, size 42
        Assert.Equal(1, table.InsertCount);
        Assert.Equal(1, table.Count);

        table.Insert(":path", "/index.html"); // index 1, size 43 → evicts index 0
        Assert.Equal(2, table.InsertCount);
        Assert.Equal(1, table.Count);

        // Index 0 should be evicted
        Assert.Null(table.GetEntry(0));
        Assert.NotNull(table.GetEntry(1));
    }

    /// RFC 9204 §3.2.1 — 32-byte per-entry overhead applied
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-3.2")]
    public void Should_Include32ByteOverhead_When_CalculatingEntrySize()
    {
        // ":method" = 7 bytes, "GET" = 3 bytes → 7 + 3 + 32 = 42
        var size = QpackDynamicTable.CalculateEntrySize(":method", "GET");
        Assert.Equal(42, size);

        // empty name and value → 0 + 0 + 32 = 32
        var emptySize = QpackDynamicTable.CalculateEntrySize("", "");
        Assert.Equal(32, emptySize);
    }

    /// RFC 9204 §3.2.3 — Capacity management triggers eviction
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-3.2")]
    public void Should_EvictOldest_When_CapacityReduced()
    {
        var table = new QpackDynamicTable(4096);

        table.Insert(":method", "GET");       // index 0, size 42
        table.Insert(":path", "/");            // index 1, size 38
        table.Insert("host", "example.com");   // index 2, size 47

        Assert.Equal(3, table.Count);

        // Reduce capacity to fit only "host" (47 bytes)
        table.SetCapacity(50);

        Assert.Equal(1, table.Count);
        Assert.Null(table.GetEntry(0));
        Assert.Null(table.GetEntry(1));
        Assert.NotNull(table.GetEntry(2));
    }

    /// RFC 9204 §3.2.2 — Oversized entry clears table, returns -1
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-3.2")]
    public void Should_ClearTableAndReturnNegative_When_EntryExceedsCapacity()
    {
        var table = new QpackDynamicTable(40);

        table.Insert("a", "b"); // size 34, fits
        Assert.Equal(1, table.Count);

        // This entry is 42 bytes, exceeds capacity of 40
        var result = table.Insert(":method", "GET");

        Assert.Equal(-1, result);
        Assert.Equal(0, table.Count);
        Assert.Equal(0, table.CurrentSize);
        Assert.Equal(1, table.InsertCount); // failed insert does not increment
    }

    /// RFC 9204 §3.2.2 — Duplicate creates new entry with new absolute index
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-3.2")]
    public void Should_CreateNewEntry_When_Duplicating()
    {
        var table = new QpackDynamicTable(4096);

        var idx0 = table.Insert(":method", "GET"); // index 0
        var idx1 = table.Duplicate(0);              // index 1

        Assert.Equal(0, idx0);
        Assert.Equal(1, idx1);
        Assert.Equal(2, table.InsertCount);

        var original = table.GetEntry(0);
        var duplicate = table.GetEntry(1);

        Assert.NotNull(original);
        Assert.NotNull(duplicate);
        Assert.Equal(original.Value.Name, duplicate.Value.Name);
        Assert.Equal(original.Value.Value, duplicate.Value.Value);
    }

    /// RFC 9204 §3.2 — Evicted entries return null, valid range tracked via DroppedCount
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-3.2")]
    public void Should_ReturnNull_When_EntryEvicted()
    {
        var table = new QpackDynamicTable(80);

        table.Insert("a", "value1"); // index 0, size 38+32=70... actually "a"=1, "value1"=6 → 1+6+32=39
        table.Insert("b", "value2"); // index 1, size 39 → total 78, fits

        Assert.Equal(2, table.Count);

        table.Insert("c", "value3"); // index 2, size 39 → total would be 117, evicts index 0
        Assert.Equal(2, table.Count);

        Assert.Null(table.GetEntry(0));
        Assert.NotNull(table.GetEntry(1));
        Assert.NotNull(table.GetEntry(2));
    }
}
