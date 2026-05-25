using System.Collections;

namespace TurboHTTP.Server;

internal sealed class TurboUrlCollection : ICollection<string>
{
    private readonly TurboServerOptions _options;

    internal TurboUrlCollection(TurboServerOptions options)
    {
        _options = options;
    }

    public int Count => _options.Urls.Count;

    public bool IsReadOnly => false;

    public void Add(string url)
    {
        _options.Urls.Add(url);
    }

    public bool Remove(string url)
    {
        return _options.Urls.Remove(url);
    }

    public void Clear()
    {
        _options.Urls.Clear();
    }

    public bool Contains(string url)
    {
        return _options.Urls.Contains(url);
    }

    public void CopyTo(string[] array, int arrayIndex)
    {
        _options.Urls.CopyTo(array, arrayIndex);
    }

    public IEnumerator<string> GetEnumerator()
    {
        return _options.Urls.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }
}
