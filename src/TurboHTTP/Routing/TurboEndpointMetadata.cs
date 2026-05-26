namespace TurboHTTP.Routing;

public sealed class TurboEndpointMetadata
{
    private readonly IReadOnlyList<object> _items;

    public TurboEndpointMetadata(IReadOnlyList<object> items)
    {
        _items = items;
    }

    public IReadOnlyList<object> Items => _items;

    public T? GetMetadata<T>() where T : class
    {
        for (var i = _items.Count - 1; i >= 0; i--)
        {
            if (_items[i] is T match)
            {
                return match;
            }
        }
        return null;
    }

    public IEnumerable<T> GetOrderedMetadata<T>() where T : class
    {
        for (var i = 0; i < _items.Count; i++)
        {
            if (_items[i] is T match)
            {
                yield return match;
            }
        }
    }

    public bool HasMetadata<T>() where T : class
    {
        for (var i = 0; i < _items.Count; i++)
        {
            if (_items[i] is T)
            {
                return true;
            }
        }
        return false;
    }

    public static TurboEndpointMetadata Merge(TurboEndpointMetadata group, TurboEndpointMetadata route)
    {
        var merged = new List<object>(group._items.Count + route._items.Count);
        merged.AddRange(group._items);
        merged.AddRange(route._items);
        return new TurboEndpointMetadata(merged);
    }
}
