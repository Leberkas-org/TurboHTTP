using System.Collections;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Primitives;

namespace TurboHTTP.Context.Adapters;

internal sealed class TurboQueryCollection : IQueryCollection
{
    private readonly Dictionary<string, StringValues> _store;

    public TurboQueryCollection(string? queryString)
    {
        if (string.IsNullOrEmpty(queryString))
        {
            _store = new Dictionary<string, StringValues>(StringComparer.OrdinalIgnoreCase);
            return;
        }

        var parsed = QueryHelpers.ParseQuery(queryString);
        _store = new Dictionary<string, StringValues>(parsed, StringComparer.OrdinalIgnoreCase);
    }

    public StringValues this[string key]
        => _store.TryGetValue(key, out var value) ? value : StringValues.Empty;

    public int Count => _store.Count;
    public ICollection<string> Keys => _store.Keys;
    public bool ContainsKey(string key) => _store.ContainsKey(key);
    public bool TryGetValue(string key, out StringValues value) => _store.TryGetValue(key, out value);
    public IEnumerator<KeyValuePair<string, StringValues>> GetEnumerator() => _store.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}