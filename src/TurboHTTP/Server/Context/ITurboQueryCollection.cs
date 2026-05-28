using Microsoft.Extensions.Primitives;

namespace TurboHTTP.Server.Context;

public interface ITurboQueryCollection : IEnumerable<KeyValuePair<string, StringValues>>
{
    StringValues this[string key] { get; }
    int Count { get; }
    ICollection<string> Keys { get; }
    bool ContainsKey(string key);
    bool TryGetValue(string key, out StringValues value);
}
