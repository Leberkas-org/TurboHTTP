using System.Buffers;
using System.Net;
using TurboHTTP.Features.Caching;

namespace TurboHTTP.Tests.Features.Caching;

public sealed class MemoryCacheStoreSpec
{
    private static readonly DateTimeOffset BaseTime = new(2024, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private static CacheStoreEntry MakeEntry()
    {
        var owner = MemoryPool<byte>.Shared.Rent(4);
        "test"u8.CopyTo(owner.Memory.Span);

        return new CacheStoreEntry
        {
            Response = new HttpResponseMessage(HttpStatusCode.OK),
            Body = new CacheBody(owner, 4),
            RequestTime = BaseTime,
            ResponseTime = BaseTime
        };
    }

    [Fact(Timeout = 5000)]
    public void TryGet_should_return_false_when_key_missing()
    {
        var store = new MemoryCacheStore();
        Assert.False(store.TryGet("missing", out _));
    }

    [Fact(Timeout = 5000)]
    public void Set_then_TryGet_should_return_stored_entry()
    {
        var store = new MemoryCacheStore();
        var entry = MakeEntry();

        store.Set("key1", entry);

        Assert.True(store.TryGet("key1", out var retrieved));
        Assert.Same(entry, retrieved);
    }

    [Fact(Timeout = 5000)]
    public void Remove_should_return_true_and_dispose_entry()
    {
        var store = new MemoryCacheStore();
        var entry = MakeEntry();
        store.Set("key1", entry);

        Assert.True(store.Remove("key1"));
        Assert.False(store.TryGet("key1", out _));
    }

    [Fact(Timeout = 5000)]
    public void Remove_should_return_false_when_key_missing()
    {
        var store = new MemoryCacheStore();
        Assert.False(store.Remove("missing"));
    }

    [Fact(Timeout = 5000)]
    public void Clear_should_remove_all_entries()
    {
        var store = new MemoryCacheStore();
        store.Set("a", MakeEntry());
        store.Set("b", MakeEntry());

        store.Clear();

        Assert.False(store.TryGet("a", out _));
        Assert.False(store.TryGet("b", out _));
    }

    [Fact(Timeout = 5000)]
    public void Dispose_should_clear_all_entries()
    {
        var store = new MemoryCacheStore();
        store.Set("a", MakeEntry());

        store.Dispose();

        Assert.False(store.TryGet("a", out _));
    }

    [Fact(Timeout = 5000)]
    public void Set_should_overwrite_existing_entry()
    {
        var store = new MemoryCacheStore();
        var entry1 = MakeEntry();
        var entry2 = MakeEntry();

        store.Set("key1", entry1);
        store.Set("key1", entry2);

        Assert.True(store.TryGet("key1", out var retrieved));
        Assert.Same(entry2, retrieved);
    }
}