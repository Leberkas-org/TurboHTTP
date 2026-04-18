using System.Collections.Frozen;
using System.Text;

namespace TurboHTTP.Protocol.Http2.Hpack;

/// <summary>
/// RFC 7541 Appendix A - Static Table.
/// 61 predefined header entries at indices 1-61.
/// Index 0 is reserved and must never be referenced.
/// </summary>
internal static class HpackStaticTable
{
    public const int StaticCount = 61;

    // Index 0 is intentionally empty (reserved, RFC 7541 §2.3.3)
    /// <summary>
    /// O(1) FrozenDictionary lookup: header name (case-insensitive) → first 1-based static table index.
    /// Static table entries with the same name are consecutive, so callers can walk forward from
    /// this index to find a full (name, value) match without scanning all 61 entries.
    /// </summary>
    internal static readonly FrozenDictionary<string, int> NameFirstIndex;

    /// <summary>
    /// Pre-computed UTF-8 byte length of each static table entry's name.
    /// Index 0 is unused (reserved). Populated at static initialization.
    /// </summary>
    internal static readonly int[] NameByteLengths;

    /// <summary>
    /// Pre-computed RFC 7541 §4.1 encoded size (nameBytes + valueBytes + 32) for each static entry.
    /// Index 0 is unused (reserved). Populated at static initialization.
    /// </summary>
    internal static readonly int[] EncodedSizes;

    static HpackStaticTable()
    {
        var dict = new Dictionary<string, int>(StaticCount, StringComparer.OrdinalIgnoreCase);
        NameByteLengths = new int[StaticCount + 1];
        EncodedSizes = new int[StaticCount + 1];

        for (var i = 1; i <= StaticCount; i++)
        {
            dict.TryAdd(Entries[i].Name, i); // first occurrence wins — entries are 1-based

            // Precompute name and entry sizes so the decoder never calls GetByteCount on static entries.
            var nameBytes = Encoding.UTF8.GetByteCount(Entries[i].Name);
            var valueBytes = Encoding.UTF8.GetByteCount(Entries[i].Value);
            NameByteLengths[i] = nameBytes;
            EncodedSizes[i] = nameBytes + valueBytes + 32;
        }

        NameFirstIndex = dict.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
    }

    public static readonly (string Name, string Value)[] Entries =
    [
        (string.Empty, string.Empty), // [0]  reserved
        (":authority", string.Empty), // [1]
        (":method", "GET"), // [2]
        (":method", "POST"), // [3]
        (":path", "/"), // [4]
        (":path", "/index.html"), // [5]
        (":scheme", "http"), // [6]
        (":scheme", "https"), // [7]
        (":status", "200"), // [8]
        (":status", "204"), // [9]
        (":status", "206"), // [10]
        (":status", "304"), // [11]
        (":status", "400"), // [12]
        (":status", "404"), // [13]
        (":status", "500"), // [14]
        ("accept-charset", string.Empty), // [15]
        ("accept-encoding", "gzip, deflate"), // [16]
        ("accept-language", string.Empty), // [17]
        ("accept-ranges", string.Empty), // [18]
        ("accept", string.Empty), // [19]
        ("access-control-allow-origin", string.Empty), // [20]
        ("age", string.Empty), // [21]
        ("allow", string.Empty), // [22]
        ("authorization", string.Empty), // [23]
        ("cache-control", string.Empty), // [24]
        ("content-disposition", string.Empty), // [25]
        ("content-encoding", string.Empty), // [26]
        ("content-language", string.Empty), // [27]
        ("content-length", string.Empty), // [28]
        ("content-location", string.Empty), // [29]
        ("content-range", string.Empty), // [30]
        ("content-type", string.Empty), // [31]
        ("cookie", string.Empty), // [32]
        ("date", string.Empty), // [33]
        ("etag", string.Empty), // [34]
        ("expect", string.Empty), // [35]
        ("expires", string.Empty), // [36]
        ("from", string.Empty), // [37]
        ("host", string.Empty), // [38]
        ("if-match", string.Empty), // [39]
        ("if-modified-since", string.Empty), // [40]
        ("if-none-match", string.Empty), // [41]
        ("if-range", string.Empty), // [42]
        ("if-unmodified-since", string.Empty), // [43]
        ("last-modified", string.Empty), // [44]
        ("link", string.Empty), // [45]
        ("location", string.Empty), // [46]
        ("max-forwards", string.Empty), // [47]
        ("proxy-authenticate", string.Empty), // [48]
        ("proxy-authorization", string.Empty), // [49]
        ("range", string.Empty), // [50]
        ("referer", string.Empty), // [51]
        ("refresh", string.Empty), // [52]
        ("retry-after", string.Empty), // [53]
        ("server", string.Empty), // [54]
        ("set-cookie", string.Empty), // [55]
        ("strict-transport-security", string.Empty), // [56]
        ("transfer-encoding", string.Empty), // [57]
        ("user-agent", string.Empty), // [58]
        ("vary", string.Empty), // [59]
        ("via", string.Empty), // [60]
        ("www-authenticate", string.Empty) // [61]
    ];
}