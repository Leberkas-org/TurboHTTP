using System.Collections;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using TurboHTTP.Protocol;
using TurboHTTP.Protocol.Semantics;

namespace TurboHTTP.Server.Context;

public interface ITurboHeaderDictionary : IHeaderDictionary;

internal sealed class TurboResponseHeaderDictionary : ITurboHeaderDictionary
{
    private readonly Dictionary<string, StringValues> _headers =
        new(StringComparer.OrdinalIgnoreCase);

    public StringValues this[string key]
    {
        get => _headers.TryGetValue(key, out var value) ? value : StringValues.Empty;
        set
        {
            if (StringValues.IsNullOrEmpty(value))
            {
                _headers.Remove(key);
            }
            else
            {
                _headers[key] = value;
            }
        }
    }

    public long? ContentLength
    {
        get
        {
            if (_headers.TryGetValue(WellKnownHeaders.ContentLength, out var value)
                && value.Count > 0
                && long.TryParse(value[0], out var length))
            {
                return length;
            }

            return null;
        }
        set
        {
            if (value.HasValue)
            {
                _headers[WellKnownHeaders.ContentLength] = ContentLengthCache.GetValue(value.Value);
            }
            else
            {
                _headers.Remove(WellKnownHeaders.ContentLength);
            }
        }
    }

    public int Count => _headers.Count;

    public bool IsReadOnly => false;

    public ICollection<string> Keys => _headers.Keys;

    public ICollection<StringValues> Values => _headers.Values;

    public void Add(string key, StringValues value)
    {
        _headers.Add(key, value);
    }

    public void Add(KeyValuePair<string, StringValues> item)
    {
        _headers.Add(item.Key, item.Value);
    }

    public void Clear()
    {
        _headers.Clear();
    }

    public bool Contains(KeyValuePair<string, StringValues> item)
    {
        return _headers.TryGetValue(item.Key, out var value) && value.Equals(item.Value);
    }

    public bool ContainsKey(string key)
    {
        return _headers.ContainsKey(key);
    }

    public void CopyTo(KeyValuePair<string, StringValues>[] array, int arrayIndex)
    {
        ((ICollection<KeyValuePair<string, StringValues>>)_headers).CopyTo(array, arrayIndex);
    }

    public IEnumerator<KeyValuePair<string, StringValues>> GetEnumerator()
    {
        return _headers.GetEnumerator();
    }

    public bool Remove(string key)
    {
        return _headers.Remove(key);
    }

    public bool Remove(KeyValuePair<string, StringValues> item)
    {
        if (_headers.TryGetValue(item.Key, out var value) && value.Equals(item.Value))
        {
            return _headers.Remove(item.Key);
        }

        return false;
    }

    public bool TryGetValue(string key, out StringValues value)
    {
        return _headers.TryGetValue(key, out value);
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    internal void Reset()
    {
        _headers.Clear();
    }
}