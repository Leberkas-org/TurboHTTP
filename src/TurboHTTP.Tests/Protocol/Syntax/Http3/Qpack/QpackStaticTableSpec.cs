using TurboHTTP.Protocol.Syntax.Http3.Qpack;

namespace TurboHTTP.Tests.Protocol.Syntax.Http3.Qpack;

public sealed class QpackStaticTableSpec
{
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-A")]
    public void Should_HaveExactly99Entries()
    {
        Assert.Equal(99, QpackStaticTable.Count);
        Assert.Equal(99, QpackStaticTable.Entries.Length);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-A")]
    public void Should_HaveAuthorityAtIndex0()
    {
        var entry = QpackStaticTable.Entries[0];
        Assert.Equal(":authority", entry.Name);
        Assert.Equal(string.Empty, entry.Value);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-A")]
    public void Should_HaveXFrameOptionsSameoriginAtIndex98()
    {
        var entry = QpackStaticTable.Entries[98];
        Assert.Equal("x-frame-options", entry.Name);
        Assert.Equal("sameorigin", entry.Value);
    }

    [Theory(Timeout = 5000)]
    [Trait("RFC", "RFC9204-A")]
    [MemberData(nameof(AllStaticEntries))]
    public void Should_HaveCorrectNameAndValue(int index, string expectedName, string expectedValue)
    {
        var entry = QpackStaticTable.Entries[index];
        Assert.Equal(expectedName, entry.Name);
        Assert.Equal(expectedValue, entry.Value);
    }

    [Theory(Timeout = 5000)]
    [Trait("RFC", "RFC9204-3.1")]
    [InlineData(":authority", "", 0)]
    [InlineData(":path", "/", 1)]
    [InlineData(":method", "GET", 17)]
    [InlineData(":method", "POST", 20)]
    [InlineData(":status", "200", 25)]
    [InlineData(":status", "404", 27)]
    [InlineData("content-type", "application/json", 46)]
    [InlineData("accept-encoding", "gzip, deflate, br", 31)]
    [InlineData("x-frame-options", "deny", 97)]
    [InlineData("x-frame-options", "sameorigin", 98)]
    public void Should_ReturnCorrectIndex_WhenFindExact(string name, string value, int expectedIndex)
    {
        Assert.Equal(expectedIndex, QpackStaticTable.FindExact(name, value));
    }

    [Theory(Timeout = 5000)]
    [Trait("RFC", "RFC9204-3.1")]
    [InlineData(":method", "PATCH")]
    [InlineData("x-custom", "value")]
    [InlineData(":status", "201")]
    public void Should_ReturnNegativeOne_WhenFindExactNotFound(string name, string value)
    {
        Assert.Equal(-1, QpackStaticTable.FindExact(name, value));
    }

    [Theory(Timeout = 5000)]
    [Trait("RFC", "RFC9204-3.1")]
    [InlineData(":authority", 0)]
    [InlineData(":path", 1)]
    [InlineData(":method", 15)]
    [InlineData(":scheme", 22)]
    [InlineData(":status", 24)]
    [InlineData("content-type", 44)]
    [InlineData("cache-control", 36)]
    [InlineData("x-frame-options", 97)]
    public void Should_ReturnLowestIndex_WhenFindName(string name, int expectedIndex)
    {
        Assert.Equal(expectedIndex, QpackStaticTable.FindName(name));
    }

    [Theory(Timeout = 5000)]
    [Trait("RFC", "RFC9204-3.1")]
    [InlineData("x-custom")]
    [InlineData("host")]
    [InlineData("transfer-encoding")]
    public void Should_ReturnNegativeOne_WhenFindNameNotFound(string name)
    {
        Assert.Equal(-1, QpackStaticTable.FindName(name));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-A")]
    public void Should_HavePseudoHeadersAtExpectedIndices()
    {
        // :authority at 0
        Assert.Equal(":authority", QpackStaticTable.Entries[0].Name);
        // :path at 1
        Assert.Equal(":path", QpackStaticTable.Entries[1].Name);
        // :method block starts at 15
        Assert.Equal(":method", QpackStaticTable.Entries[15].Name);
        Assert.Equal("CONNECT", QpackStaticTable.Entries[15].Value);
        // :scheme at 22-23
        Assert.Equal(":scheme", QpackStaticTable.Entries[22].Name);
        Assert.Equal("http", QpackStaticTable.Entries[22].Value);
        Assert.Equal(":scheme", QpackStaticTable.Entries[23].Name);
        Assert.Equal("https", QpackStaticTable.Entries[23].Value);
        // :status block starts at 24
        Assert.Equal(":status", QpackStaticTable.Entries[24].Name);
        Assert.Equal("103", QpackStaticTable.Entries[24].Value);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-A")]
    public void Should_FindExactMatch_ForAllEntriesWithValues()
    {
        for (var i = 0; i < QpackStaticTable.Count; i++)
        {
            var entry = QpackStaticTable.Entries[i];
            var found = QpackStaticTable.FindExact(entry.Name, entry.Value);
            Assert.True(found >= 0,
                $"Expected FindExact to find index for ({entry.Name}, {entry.Value}) at index {i}");
            // FindExact returns the first matching index, which may differ from i
            // if there are duplicate (name, value) pairs — but RFC 9204 has no duplicates
            Assert.Equal(i, found);
        }
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-A")]
    public void Should_FindNameMatch_ForAllEntries()
    {
        for (var i = 0; i < QpackStaticTable.Count; i++)
        {
            var entry = QpackStaticTable.Entries[i];
            var found = QpackStaticTable.FindName(entry.Name);
            Assert.True(found >= 0,
                $"Expected FindName to find index for name '{entry.Name}' at entry {i}");
            // FindName returns the lowest index for the name
            Assert.True(found <= i,
                $"Expected FindName to return index <= {i} for name '{entry.Name}', got {found}");
        }
    }

    public static TheoryData<int, string, string> AllStaticEntries()
    {
        return new TheoryData<int, string, string>
        {
            { 0, ":authority", "" },
            { 1, ":path", "/" },
            { 2, "age", "0" },
            { 3, "content-disposition", "" },
            { 4, "content-length", "0" },
            { 5, "cookie", "" },
            { 6, "date", "" },
            { 7, "etag", "" },
            { 8, "if-modified-since", "" },
            { 9, "if-none-match", "" },
            { 10, "last-modified", "" },
            { 11, "link", "" },
            { 12, "location", "" },
            { 13, "referer", "" },
            { 14, "set-cookie", "" },
            { 15, ":method", "CONNECT" },
            { 16, ":method", "DELETE" },
            { 17, ":method", "GET" },
            { 18, ":method", "HEAD" },
            { 19, ":method", "OPTIONS" },
            { 20, ":method", "POST" },
            { 21, ":method", "PUT" },
            { 22, ":scheme", "http" },
            { 23, ":scheme", "https" },
            { 24, ":status", "103" },
            { 25, ":status", "200" },
            { 26, ":status", "304" },
            { 27, ":status", "404" },
            { 28, ":status", "503" },
            { 29, "accept", "*/*" },
            { 30, "accept", "application/dns-message" },
            { 31, "accept-encoding", "gzip, deflate, br" },
            { 32, "accept-ranges", "bytes" },
            { 33, "access-control-allow-headers", "cache-control" },
            { 34, "access-control-allow-headers", "content-type" },
            { 35, "access-control-allow-origin", "*" },
            { 36, "cache-control", "max-age=0" },
            { 37, "cache-control", "max-age=2592000" },
            { 38, "cache-control", "max-age=604800" },
            { 39, "cache-control", "no-cache" },
            { 40, "cache-control", "no-store" },
            { 41, "cache-control", "public, max-age=31536000" },
            { 42, "content-encoding", "br" },
            { 43, "content-encoding", "gzip" },
            { 44, "content-type", "application/dns-message" },
            { 45, "content-type", "application/javascript" },
            { 46, "content-type", "application/json" },
            { 47, "content-type", "application/x-www-form-urlencoded" },
            { 48, "content-type", "image/gif" },
            { 49, "content-type", "image/jpeg" },
            { 50, "content-type", "image/png" },
            { 51, "content-type", "text/css" },
            { 52, "content-type", "text/html; charset=utf-8" },
            { 53, "content-type", "text/plain" },
            { 54, "content-type", "text/plain;charset=utf-8" },
            { 55, "range", "bytes=0-" },
            { 56, "strict-transport-security", "max-age=31536000" },
            { 57, "strict-transport-security", "max-age=31536000; includesubdomains" },
            { 58, "strict-transport-security", "max-age=31536000; includesubdomains; preload" },
            { 59, "vary", "accept-encoding" },
            { 60, "vary", "origin" },
            { 61, "x-content-type-options", "nosniff" },
            { 62, "x-xss-protection", "1; mode=block" },
            { 63, ":status", "100" },
            { 64, ":status", "204" },
            { 65, ":status", "206" },
            { 66, ":status", "302" },
            { 67, ":status", "400" },
            { 68, ":status", "403" },
            { 69, ":status", "421" },
            { 70, ":status", "425" },
            { 71, ":status", "500" },
            { 72, "accept-language", "" },
            { 73, "access-control-allow-credentials", "FALSE" },
            { 74, "access-control-allow-credentials", "TRUE" },
            { 75, "access-control-allow-headers", "*" },
            { 76, "access-control-allow-methods", "get" },
            { 77, "access-control-allow-methods", "get, post, options" },
            { 78, "access-control-allow-methods", "options" },
            { 79, "access-control-expose-headers", "content-length" },
            { 80, "access-control-request-headers", "content-type" },
            { 81, "access-control-request-method", "get" },
            { 82, "access-control-request-method", "post" },
            { 83, "alt-svc", "clear" },
            { 84, "authorization", "" },
            { 85, "content-security-policy", "script-src 'none'; object-src 'none'; base-uri 'none'" },
            { 86, "early-data", "1" },
            { 87, "expect-ct", "" },
            { 88, "forwarded", "" },
            { 89, "if-range", "" },
            { 90, "origin", "" },
            { 91, "purpose", "prefetch" },
            { 92, "server", "" },
            { 93, "timing-allow-origin", "*" },
            { 94, "upgrade-insecure-requests", "1" },
            { 95, "user-agent", "" },
            { 96, "x-forwarded-for", "" },
            { 97, "x-frame-options", "deny" },
            { 98, "x-frame-options", "sameorigin" },
        };
    }
}