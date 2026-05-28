using Microsoft.Extensions.Primitives;

namespace TurboHTTP.Server.Context;

public interface ITurboFormCollection : IEnumerable<KeyValuePair<string, StringValues>>
{
    StringValues this[string key] { get; }
    int Count { get; }
    ICollection<string> Keys { get; }
    bool ContainsKey(string key);
    ITurboFormFileCollection Files { get; }
}

public interface ITurboFormFileCollection : IEnumerable<ITurboFormFile>
{
    ITurboFormFile this[int index] { get; }
    ITurboFormFile? this[string name] { get; }
    int Count { get; }
    ITurboFormFile? GetFile(string name);
    IReadOnlyList<ITurboFormFile> GetFiles(string name);
}
