using System.Collections;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace TurboHTTP.Server.Context;

internal sealed class TurboFormCollection(Dictionary<string, StringValues> fields, IFormFileCollection files) : IFormCollection, ITurboFormCollection
{
    public StringValues this[string key]
        => fields.TryGetValue(key, out var value) ? value : StringValues.Empty;

    public int Count => fields.Count;
    public ICollection<string> Keys => fields.Keys;
    public IFormFileCollection Files { get; } = files;

    public bool ContainsKey(string key) => fields.ContainsKey(key);
    public bool TryGetValue(string key, out StringValues value) => fields.TryGetValue(key, out value);
    public IEnumerator<KeyValuePair<string, StringValues>> GetEnumerator() => fields.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    ITurboFormFileCollection ITurboFormCollection.Files => (ITurboFormFileCollection)files;
}

internal sealed class TurboFormFileCollection : IFormFileCollection, ITurboFormFileCollection
{
    private readonly List<IFormFile> _files;

    public TurboFormFileCollection(List<IFormFile> files)
    {
        _files = files;
    }

    public IFormFile this[int index] => _files[index];

    public IFormFile? this[string name] => _files.FirstOrDefault(f =>
        string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));

    public int Count => _files.Count;
    public IFormFile? GetFile(string name) => this[name];

    public IReadOnlyList<IFormFile> GetFiles(string name)
        => _files.Where(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase)).ToList();

    public IEnumerator<IFormFile> GetEnumerator() => _files.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    ITurboFormFile ITurboFormFileCollection.this[int index] => (ITurboFormFile)_files[index];

    ITurboFormFile? ITurboFormFileCollection.this[string name] => (ITurboFormFile?)_files.FirstOrDefault(f =>
        string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));

    ITurboFormFile? ITurboFormFileCollection.GetFile(string name) => (ITurboFormFile?)this[name];

    IReadOnlyList<ITurboFormFile> ITurboFormFileCollection.GetFiles(string name)
        => _files.Where(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase)).ToList().Cast<ITurboFormFile>().ToList();

    IEnumerator<ITurboFormFile> IEnumerable<ITurboFormFile>.GetEnumerator()
        => _files.Cast<ITurboFormFile>().GetEnumerator();
}