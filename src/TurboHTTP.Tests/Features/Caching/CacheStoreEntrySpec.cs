using System.Buffers;
using System.Net;
using TurboHTTP.Features.Caching;

namespace TurboHTTP.Tests.Features.Caching;

public sealed class CacheStoreEntrySpec
{
    private static readonly DateTimeOffset BaseTime = new(2024, 1, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact(Timeout = 5000)]
    public void CacheStoreEntry_should_dispose_body_on_dispose()
    {
        var owner = MemoryPool<byte>.Shared.Rent(16);
        "test"u8.CopyTo(owner.Memory.Span);

        var entry = new CacheStoreEntry
        {
            Response = new HttpResponseMessage(HttpStatusCode.OK),
            Body = new CacheBody(owner, 4),
            RequestTime = BaseTime,
            ResponseTime = BaseTime
        };

        entry.Dispose();

        Assert.True(entry.Body.Memory.IsEmpty);
    }

    [Fact(Timeout = 5000)]
    public void CacheControlStoreEntry_should_round_trip_to_CacheControl()
    {
        var store = new CacheControlStoreEntry
        {
            NoCache = true,
            NoStore = false,
            MaxAge = TimeSpan.FromSeconds(300),
            MustRevalidate = true,
            Public = true,
            Private = false,
            Immutable = true,
            SMaxAge = TimeSpan.FromSeconds(600),
            NoCacheFields = ["x-custom"],
            PrivateFields = ["x-private"],
            MustUnderstand = true
        };

        var cc = store.ToCacheControl();

        Assert.True(cc.NoCache);
        Assert.False(cc.NoStore);
        Assert.Equal(TimeSpan.FromSeconds(300), cc.MaxAge);
        Assert.True(cc.MustRevalidate);
        Assert.True(cc.Public);
        Assert.False(cc.Private);
        Assert.True(cc.Immutable);
        Assert.Equal(TimeSpan.FromSeconds(600), cc.SMaxAge);
        Assert.True(cc.MustUnderstand);
    }

    [Fact(Timeout = 5000)]
    public void CacheControlStoreEntry_should_round_trip_from_CacheControl()
    {
        var cc = new CacheControl
        {
            NoCache = false,
            NoStore = true,
            MaxAge = TimeSpan.FromSeconds(120),
            ProxyRevalidate = true,
            NoTransform = true
        };

        var store = CacheControlStoreEntry.FromCacheControl(cc);

        Assert.False(store.NoCache);
        Assert.True(store.NoStore);
        Assert.Equal(TimeSpan.FromSeconds(120), store.MaxAge);
        Assert.True(store.ProxyRevalidate);
        Assert.True(store.NoTransform);
    }

    [Fact(Timeout = 5000)]
    public void CacheControlStoreEntry_FromCacheControl_should_handle_null_fields()
    {
        var cc = new CacheControl();

        var store = CacheControlStoreEntry.FromCacheControl(cc);

        Assert.Empty(store.NoCacheFields);
        Assert.Empty(store.PrivateFields);
    }
}