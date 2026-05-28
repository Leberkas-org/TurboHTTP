using Microsoft.Extensions.Primitives;

namespace TurboHTTP.Server.Context;

public interface ITurboHeaderDictionary : IEnumerable<KeyValuePair<string, StringValues>>
{
    StringValues this[string key] { get; set; }
    long? ContentLength { get; set; }
    int Count { get; }
    ICollection<string> Keys { get; }
    bool ContainsKey(string key);
    bool TryGetValue(string key, out StringValues value);
    void Add(string key, StringValues value);
    bool Remove(string key);
    void Clear();
}
