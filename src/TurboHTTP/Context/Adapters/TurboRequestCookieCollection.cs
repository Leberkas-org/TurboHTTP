using System.Collections;
using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Http;

namespace TurboHTTP.Context.Adapters;

internal sealed class TurboRequestCookieCollection : IRequestCookieCollection
{
    private readonly Dictionary<string, string?> _cookies;

    public TurboRequestCookieCollection(string? cookieHeader)
    {
        _cookies = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(cookieHeader))
        {
            return;
        }

        foreach (var segment in cookieHeader.Split(';',
                     StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var eqIdx = segment.IndexOf('=');
            if (eqIdx > 0)
            {
                var name = segment[..eqIdx].Trim();
                var value = segment[(eqIdx + 1)..].Trim();
                _cookies[name] = value;
            }
        }
    }

    public string? this[string key] => _cookies.GetValueOrDefault(key);

    public int Count => _cookies.Count;
    public ICollection<string> Keys => _cookies.Keys;
    public bool ContainsKey(string key) => _cookies.ContainsKey(key);
    public bool TryGetValue(string key, [NotNullWhen(true)] out string? value) => _cookies.TryGetValue(key, out value);
    public IEnumerator<KeyValuePair<string, string>> GetEnumerator() => _cookies.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}