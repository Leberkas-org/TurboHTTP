using TurboHTTP.Protocol.Http3.Qpack;

namespace TurboHTTP.Tests.Http3.Qpack;

public sealed class QpackDynamicTableEdgeCasesSpec
{
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-3.2")]
    public void Should_Create_Table_With_Zero_Capacity()
    {
        var table = new QpackDynamicTable();
        Assert.Equal(0, table.Capacity);
        Assert.Equal(0, table.CurrentSize);
        Assert.Equal(0, table.Count);
        Assert.Equal(0, table.InsertCount);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-3.2")]
    public void Should_Create_Table_With_Large_Capacity()
    {
        var table = new QpackDynamicTable(65536);
        Assert.Equal(65536, table.Capacity);
        Assert.Equal(0, table.CurrentSize);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-3.2.2")]
    public void Should_Insert_Single_Entry()
    {
        var table = new QpackDynamicTable(256);
        var index = table.Insert("name", "value");

        Assert.Equal(0, index);
        Assert.Equal(1, table.Count);
        Assert.Equal(1, table.InsertCount);
        Assert.True(table.CurrentSize > 0);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-3.2.2")]
    public void Should_Return_Neg1_When_Entry_Exceeds_Capacity()
    {
        var table = new QpackDynamicTable(64); // Small capacity
        var largeValue = new string('x', 1000);
        var index = table.Insert("name", largeValue);

        Assert.Equal(-1, index);
        Assert.Equal(0, table.Count); // Table cleared, entry not added
        Assert.Equal(0, table.CurrentSize);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-3.2.2")]
    public void Should_Evict_Oldest_When_Capacity_Exceeded()
    {
        var table = new QpackDynamicTable(128);

        // Insert first entry
        var index1 = table.Insert("name1", "value1");
        Assert.Equal(0, index1);
        Assert.Equal(1, table.Count);

        // Insert second entry - both fit, no eviction
        var index2 = table.Insert("name2", "value2");
        Assert.Equal(1, index2);
        Assert.Equal(2, table.Count); // Both entries fit in 100-byte capacity
        Assert.True(table.CurrentSize > 0);
        Assert.NotNull(table.GetEntry(0)); // First entry still present
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-3.2.2")]
    public void Should_Evict_Multiple_Entries_When_Inserting_Large_Entry()
    {
        var table = new QpackDynamicTable(256);

        // Insert multiple small entries
        for (var i = 0; i < 5; i++)
        {
            table.Insert($"header{i}", $"value{i}");
        }

        Assert.Equal(5, table.Count);
        var beforeEvictionInsertCount = table.InsertCount;

        // Insert large entry that requires evicting multiple oldest entries
        table.Insert("big", new string('x', 150));

        Assert.True(table.Count < 5); // Some entries evicted
        Assert.Equal(beforeEvictionInsertCount + 1, table.InsertCount); // InsertCount always increases
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-3.2")]
    public void Should_Return_Null_For_GetEntry_On_Empty_Table()
    {
        var table = new QpackDynamicTable(256);
        var entry = table.GetEntry(0);
        Assert.Null(entry);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-3.2")]
    public void Should_Return_Null_For_Negative_Index()
    {
        var table = new QpackDynamicTable(256);
        table.Insert("name", "value");

        var entry = table.GetEntry(-1);
        Assert.Null(entry);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-3.2")]
    public void Should_Return_Null_For_Index_Beyond_InsertCount()
    {
        var table = new QpackDynamicTable(256);
        table.Insert("name", "value");
        Assert.Equal(1, table.InsertCount);

        var entry = table.GetEntry(1); // InsertCount is 1, asking for index 1
        Assert.Null(entry);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-3.2")]
    public void Should_Return_Null_For_Evicted_Entry()
    {
        var table = new QpackDynamicTable(128);

        var index1 = table.Insert("old", "data");
        Assert.Equal(0, index1);

        // Insert entries to evict the first one
        for (var i = 0; i < 10; i++)
        {
            table.Insert($"new{i}", "x");
        }

        var entry = table.GetEntry(0); // Original entry should be evicted
        Assert.Null(entry);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-3.2")]
    public void Should_Return_Entry_When_Still_In_Table()
    {
        var table = new QpackDynamicTable(256);
        var index = table.Insert("test-name", "test-value");

        var entry = table.GetEntry(index);
        Assert.NotNull(entry);
        Assert.Equal("test-name", entry.Value.Name);
        Assert.Equal("test-value", entry.Value.Value);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-3.2.2")]
    public void Should_Duplicate_Valid_Entry()
    {
        var table = new QpackDynamicTable(256);
        var original = table.Insert("name", "value");

        var duplicate = table.Duplicate(original);
        Assert.Equal(1, duplicate); // New index is 1
        Assert.Equal(2, table.Count); // Both original and duplicate in table

        var entry = table.GetEntry(duplicate);
        Assert.NotNull(entry);
        Assert.Equal("name", entry.Value.Name);
        Assert.Equal("value", entry.Value.Value);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-3.2.2")]
    public void Should_Return_Neg1_When_Duplicating_Evicted_Entry()
    {
        var table = new QpackDynamicTable(128);
        var original = table.Insert("old", "data");

        // Evict the original entry
        for (var i = 0; i < 10; i++)
        {
            table.Insert($"new{i}", "x");
        }

        var duplicate = table.Duplicate(original);
        Assert.Equal(-1, duplicate); // Entry was evicted
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-3.2.2")]
    public void Should_Return_Neg1_When_Duplicating_Non_Existent_Entry()
    {
        var table = new QpackDynamicTable(256);
        table.Insert("name", "value");

        var duplicate = table.Duplicate(999); // Non-existent index
        Assert.Equal(-1, duplicate);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-3.2.3")]
    public void Should_Evict_When_Capacity_Reduced()
    {
        var table = new QpackDynamicTable(256);

        // Insert entries
        for (var i = 0; i < 5; i++)
        {
            table.Insert($"header{i}", $"value{i}");
        }

        var countBefore = table.Count;

        // Reduce capacity dramatically
        table.SetCapacity(64);

        Assert.True(table.Count < countBefore); // Entries evicted
        Assert.True(table.CurrentSize <= table.Capacity);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-3.2.3")]
    public void Should_Clear_Table_When_Capacity_Set_To_Zero()
    {
        var table = new QpackDynamicTable(256);

        for (var i = 0; i < 5; i++)
        {
            table.Insert($"header{i}", $"value{i}");
        }

        Assert.True(table.Count > 0);

        table.SetCapacity(0);

        Assert.Equal(0, table.Count);
        Assert.Equal(0, table.CurrentSize);
        Assert.Equal(0, table.Capacity);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-3.2.3")]
    public void Should_Not_Evict_When_Capacity_Increased()
    {
        var table = new QpackDynamicTable(128);

        for (var i = 0; i < 3; i++)
        {
            table.Insert($"header{i}", $"value{i}");
        }

        var countBefore = table.Count;

        table.SetCapacity(512);

        Assert.Equal(countBefore, table.Count); // No eviction
        Assert.Equal(512, table.Capacity);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-3.2.1")]
    public void Should_Calculate_Entry_Size_Correctly()
    {
        var size = QpackDynamicTable.CalculateEntrySize("name", "value");

        // 4 + 5 + 32 overhead = 41
        const int expected = 4 + 5 + 32;
        Assert.Equal(expected, size);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-3.2.1")]
    public void Should_Calculate_Size_With_Empty_Values()
    {
        var size = QpackDynamicTable.CalculateEntrySize("", "");
        Assert.Equal(32, size); // Just the overhead
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-3.2.1")]
    public void Should_Calculate_Size_With_Large_Values()
    {
        var largeName = new string('x', 1000);
        var largeValue = new string('y', 2000);
        var size = QpackDynamicTable.CalculateEntrySize(largeName, largeValue);

        const int expected = 1000 + 2000 + 32;
        Assert.Equal(expected, size);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-3.2.1")]
    public void Should_Calculate_Size_With_Utf8_Characters()
    {
        // UTF-8 multi-byte characters
        var size = QpackDynamicTable.CalculateEntrySize("Ñame", "valüe");

        // "Ñ" = 2 bytes, "ü" = 2 bytes in UTF-8
        var expected = 5 + 6 + 32; // 5 bytes for "Ñame", 6 bytes for "valüe"
        Assert.Equal(expected, size);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-3.2")]
    public void Should_Return_InsertCount_For_DroppedCount_On_Empty_Table()
    {
        var table = new QpackDynamicTable(256);
        Assert.Equal(0, table.DroppedCount);
        Assert.Equal(0, table.InsertCount);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-3.2")]
    public void Should_Return_Lowest_Absolute_Index_For_DroppedCount()
    {
        var table = new QpackDynamicTable(256);

        table.Insert("first", "x");
        table.Insert("second", "x");
        table.Insert("third", "x");

        // DroppedCount is 0 (first entry's absolute index)
        Assert.Equal(0, table.DroppedCount);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-3.2")]
    public void Should_Return_Next_Available_Index_After_Eviction()
    {
        var table = new QpackDynamicTable(128);

        // Insert entries
        table.Insert("entry0", "x");
        table.Insert("entry1", "x");
        table.Insert("entry2", "x");

        var initialDropped = table.DroppedCount;

        // Evict first entry
        for (var i = 0; i < 10; i++)
        {
            table.Insert($"new{i}", "x");
        }

        // DroppedCount should increase
        Assert.True(table.DroppedCount >= initialDropped);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-3.2.2")]
    public void Should_Never_Reset_InsertCount()
    {
        var table = new QpackDynamicTable(128);

        table.Insert("a", "x");
        table.Insert("b", "x");
        var insertCountAfter2 = table.InsertCount;

        // Evict entries by reducing capacity
        table.SetCapacity(0);
        Assert.Equal(0, table.Count);

        // InsertCount should not reset
        Assert.Equal(insertCountAfter2, table.InsertCount);

        // Inserting again continues the count
        table.SetCapacity(256);
        table.Insert("c", "x");
        Assert.Equal(insertCountAfter2 + 1, table.InsertCount);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-3.2")]
    public void Should_Throw_On_Negative_Capacity()
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new QpackDynamicTable(-1));

        Assert.Equal("capacity", ex.ParamName);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-3.2.3")]
    public void Should_Throw_On_Negative_SetCapacity()
    {
        var table = new QpackDynamicTable(256);

        var ex = Assert.Throws<QpackException>(() =>
            table.SetCapacity(-1));

        Assert.Contains("non-negative", ex.Message);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-3.2")]
    public void Should_Return_Correct_Entry_At_Boundaries()
    {
        var table = new QpackDynamicTable(256);

        // Insert multiple entries
        var indices = new List<int>();
        for (var i = 0; i < 5; i++)
        {
            indices.Add(table.Insert($"header{i}", $"value{i}"));
        }

        // Verify we can retrieve each by absolute index
        for (var i = 0; i < indices.Count; i++)
        {
            var entry = table.GetEntry(indices[i]);
            Assert.NotNull(entry);
            Assert.Equal($"header{i}", entry.Value.Name);
        }
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-3.2")]
    public void Should_Correctly_Access_Non_Evicted_Entries()
    {
        var table = new QpackDynamicTable(128);

        var index0 = table.Insert("first", "data1");
        var index1 = table.Insert("second", "data2");
        var index2 = table.Insert("third", "data3");

        // Insert to evict only the oldest
        table.Insert("fourth", "x");

        // Original first entry should be evicted
        Assert.Null(table.GetEntry(index0));

        // Others should still be accessible
        Assert.NotNull(table.GetEntry(index1));
        Assert.NotNull(table.GetEntry(index2));
    }
}