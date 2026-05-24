namespace TurboHTTP.Protocol;

internal static class ContentHeaderClassifier
{
    private static readonly HashSet<string> ContentHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        WellKnownHeaders.ContentType,
        WellKnownHeaders.ContentLength,
        WellKnownHeaders.ContentEncoding,
        WellKnownHeaders.ContentLanguage,
        WellKnownHeaders.ContentLocation,
        WellKnownHeaders.ContentMd5,
        WellKnownHeaders.ContentRange,
        WellKnownHeaders.ContentDisposition,
        WellKnownHeaders.Allow,
        WellKnownHeaders.Expires,
        WellKnownHeaders.LastModified
    };

    private static readonly HashSet<string> ForbiddenConnectionHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        WellKnownHeaders.Connection,
        WellKnownHeaders.TransferEncoding,
        WellKnownHeaders.Upgrade,
        WellKnownHeaders.ProxyConnection,
        WellKnownHeaders.KeepAliveHeader,
        WellKnownHeaders.Te
    };

    private static readonly Dictionary<string, string> ForbiddenConnectionHeadersExcludingTeMap =
        new(StringComparer.OrdinalIgnoreCase)
        {
            [WellKnownHeaders.Connection] = WellKnownHeaders.Connection,
            [WellKnownHeaders.TransferEncoding] = WellKnownHeaders.TransferEncoding,
            [WellKnownHeaders.Upgrade] = WellKnownHeaders.Upgrade,
            [WellKnownHeaders.ProxyConnection] = WellKnownHeaders.ProxyConnection,
            [WellKnownHeaders.KeepAliveHeader] = WellKnownHeaders.KeepAliveHeader
        };

    public static bool IsContentHeader(string name) => ContentHeaders.Contains(name);

    public static bool IsForbiddenConnectionHeader(string name) => ForbiddenConnectionHeaders.Contains(name);

    public static bool IsForbiddenConnectionHeaderExcludingTe(string name)
        => ForbiddenConnectionHeadersExcludingTeMap.ContainsKey(name);

    public static bool TryGetForbiddenCanonicalName(string name, out string canonicalName)
        => ForbiddenConnectionHeadersExcludingTeMap.TryGetValue(name, out canonicalName!);

    private static readonly Dictionary<string, string> LowerCaseCache = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Content-Type"] = "content-type",
        ["Content-Length"] = "content-length",
        ["Content-Encoding"] = "content-encoding",
        ["Content-Language"] = "content-language",
        ["Content-Location"] = "content-location",
        ["Content-Range"] = "content-range",
        ["Content-Disposition"] = "content-disposition",
        ["Cache-Control"] = "cache-control",
        ["Date"] = "date",
        ["Server"] = "server",
        ["Set-Cookie"] = "set-cookie",
        ["Transfer-Encoding"] = "transfer-encoding",
        ["ETag"] = "etag",
        ["Last-Modified"] = "last-modified",
        ["Location"] = "location",
        ["Vary"] = "vary",
        ["Accept-Ranges"] = "accept-ranges",
        ["Access-Control-Allow-Origin"] = "access-control-allow-origin",
        ["Access-Control-Allow-Methods"] = "access-control-allow-methods",
        ["Access-Control-Allow-Headers"] = "access-control-allow-headers",
        ["X-Content-Type-Options"] = "x-content-type-options",
        ["Strict-Transport-Security"] = "strict-transport-security",
    };

    public static string ToLowerAscii(string name)
    {
        if (LowerCaseCache.TryGetValue(name, out var cached))
        {
            return cached;
        }

        if (!name.AsSpan().ContainsAnyInRange('A', 'Z'))
        {
            return name;
        }

        return string.Create(name.Length, name, static (span, src) =>
        {
            System.Text.Ascii.ToLower(src, span, out _);
        });
    }

    public static string JoinHeaderValues(IEnumerable<string> values)
    {
        using var enumerator = values.GetEnumerator();
        if (!enumerator.MoveNext())
        {
            return string.Empty;
        }

        var first = enumerator.Current;
        if (!enumerator.MoveNext())
        {
            return first;
        }

        var second = enumerator.Current;
        if (!enumerator.MoveNext())
        {
            return string.Concat(first, ", ", second);
        }

        var parts = new List<string>(4) { first, second, enumerator.Current };
        var totalLength = first.Length + second.Length + enumerator.Current.Length + 4;

        while (enumerator.MoveNext())
        {
            totalLength += 2 + enumerator.Current.Length;
            parts.Add(enumerator.Current);
        }

        return string.Create(totalLength, parts, static (span, state) =>
        {
            var pos = 0;
            state[0].AsSpan().CopyTo(span);
            pos += state[0].Length;

            for (var i = 1; i < state.Count; i++)
            {
                span[pos] = ',';
                span[pos + 1] = ' ';
                pos += 2;
                state[i].AsSpan().CopyTo(span[pos..]);
                pos += state[i].Length;
            }
        });
    }
}