using System.Collections.Frozen;

namespace TurboHTTP.Protocol.Http3.Qpack;

/// <summary>
/// RFC 9204 Appendix A - QPACK Static Table.
/// 99 predefined header entries at indices 0-98.
/// Unlike HPACK, QPACK uses 0-based indexing.
/// </summary>
public static class QpackStaticTable
{
    public const int Count = 99;

    /// <summary>
    /// All 99 static table entries indexed 0-98 per RFC 9204 Appendix A.
    /// </summary>
    public static readonly (string Name, string Value)[] Entries =
    [
        (":authority", string.Empty), // [0]
        (":path", "/"), // [1]
        ("age", "0"), // [2]
        ("content-disposition", string.Empty), // [3]
        ("content-length", "0"), // [4]
        ("cookie", string.Empty), // [5]
        ("date", string.Empty), // [6]
        ("etag", string.Empty), // [7]
        ("if-modified-since", string.Empty), // [8]
        ("if-none-match", string.Empty), // [9]
        ("last-modified", string.Empty), // [10]
        ("link", string.Empty), // [11]
        ("location", string.Empty), // [12]
        ("referer", string.Empty), // [13]
        ("set-cookie", string.Empty), // [14]
        (":method", "CONNECT"), // [15]
        (":method", "DELETE"), // [16]
        (":method", "GET"), // [17]
        (":method", "HEAD"), // [18]
        (":method", "OPTIONS"), // [19]
        (":method", "POST"), // [20]
        (":method", "PUT"), // [21]
        (":scheme", "http"), // [22]
        (":scheme", "https"), // [23]
        (":status", "103"), // [24]
        (":status", "200"), // [25]
        (":status", "304"), // [26]
        (":status", "404"), // [27]
        (":status", "503"), // [28]
        ("accept", "*/*"), // [29]
        ("accept", "application/dns-message"), // [30]
        ("accept-encoding", "gzip, deflate, br"), // [31]
        ("accept-ranges", "bytes"), // [32]
        ("access-control-allow-headers", "cache-control"), // [33]
        ("access-control-allow-headers", "content-type"), // [34]
        ("access-control-allow-origin", "*"), // [35]
        ("cache-control", "max-age=0"), // [36]
        ("cache-control", "max-age=2592000"), // [37]
        ("cache-control", "max-age=604800"), // [38]
        ("cache-control", "no-cache"), // [39]
        ("cache-control", "no-store"), // [40]
        ("cache-control", "public, max-age=31536000"), // [41]
        ("content-encoding", "br"), // [42]
        ("content-encoding", "gzip"), // [43]
        ("content-type", "application/dns-message"), // [44]
        ("content-type", "application/javascript"), // [45]
        ("content-type", "application/json"), // [46]
        ("content-type", "application/x-www-form-urlencoded"), // [47]
        ("content-type", "image/gif"), // [48]
        ("content-type", "image/jpeg"), // [49]
        ("content-type", "image/png"), // [50]
        ("content-type", "text/css"), // [51]
        ("content-type", "text/html; charset=utf-8"), // [52]
        ("content-type", "text/plain"), // [53]
        ("content-type", "text/plain;charset=utf-8"), // [54]
        ("range", "bytes=0-"), // [55]
        ("strict-transport-security", "max-age=31536000"), // [56]
        ("strict-transport-security", "max-age=31536000; includesubdomains"), // [57]
        ("strict-transport-security", "max-age=31536000; includesubdomains; preload"), // [58]
        ("vary", "accept-encoding"), // [59]
        ("vary", "origin"), // [60]
        ("x-content-type-options", "nosniff"), // [61]
        ("x-xss-protection", "1; mode=block"), // [62]
        (":status", "100"), // [63]
        (":status", "204"), // [64]
        (":status", "206"), // [65]
        (":status", "302"), // [66]
        (":status", "400"), // [67]
        (":status", "403"), // [68]
        (":status", "421"), // [69]
        (":status", "425"), // [70]
        (":status", "500"), // [71]
        ("accept-language", string.Empty), // [72]
        ("access-control-allow-credentials", "FALSE"), // [73]
        ("access-control-allow-credentials", "TRUE"), // [74]
        ("access-control-allow-headers", "*"), // [75]
        ("access-control-allow-methods", "get"), // [76]
        ("access-control-allow-methods", "get, post, options"), // [77]
        ("access-control-allow-methods", "options"), // [78]
        ("access-control-expose-headers", "content-length"), // [79]
        ("access-control-request-headers", "content-type"), // [80]
        ("access-control-request-method", "get"), // [81]
        ("access-control-request-method", "post"), // [82]
        ("alt-svc", "clear"), // [83]
        ("authorization", string.Empty), // [84]
        ("content-security-policy", "script-src 'none'; object-src 'none'; base-uri 'none'"), // [85]
        ("early-data", "1"), // [86]
        ("expect-ct", string.Empty), // [87]
        ("forwarded", string.Empty), // [88]
        ("if-range", string.Empty), // [89]
        ("origin", string.Empty), // [90]
        ("purpose", "prefetch"), // [91]
        ("server", string.Empty), // [92]
        ("timing-allow-origin", "*"), // [93]
        ("upgrade-insecure-requests", "1"), // [94]
        ("user-agent", string.Empty), // [95]
        ("x-forwarded-for", string.Empty), // [96]
        ("x-frame-options", "deny"), // [97]
        ("x-frame-options", "sameorigin"), // [98]
    ];

    /// <summary>
    /// Lookup map from (name, value) to static table index for exact matches.
    /// </summary>
    private static readonly FrozenDictionary<(string Name, string Value), int> ExactIndex =
        BuildExactIndex();

    /// <summary>
    /// Lookup map from name to first static table index for name-only matches.
    /// When multiple entries share a name, returns the lowest index.
    /// </summary>
    private static readonly FrozenDictionary<string, int> NameIndex =
        BuildNameIndex();

    /// <summary>
    /// Tries to find an exact (name, value) match in the static table.
    /// </summary>
    /// <returns>The index if found, or -1 if not found.</returns>
    public static int FindExact(string name, string value)
    {
        return ExactIndex.GetValueOrDefault((name, value), -1);
    }

    /// <summary>
    /// Tries to find a name-only match in the static table.
    /// Returns the lowest index for the given name.
    /// </summary>
    /// <returns>The index if found, or -1 if not found.</returns>
    public static int FindName(string name)
    {
        return NameIndex.GetValueOrDefault(name, -1);
    }

    private static FrozenDictionary<(string Name, string Value), int> BuildExactIndex()
    {
        var dict = new Dictionary<(string, string), int>(Count);
        for (var i = 0; i < Count; i++)
        {
            var entry = Entries[i];
            dict.TryAdd((entry.Name, entry.Value), i);
        }

        return dict.ToFrozenDictionary();
    }

    private static FrozenDictionary<string, int> BuildNameIndex()
    {
        var dict = new Dictionary<string, int>(Count);
        for (var i = 0; i < Count; i++)
        {
            dict.TryAdd(Entries[i].Name, i);
        }

        return dict.ToFrozenDictionary();
    }
}