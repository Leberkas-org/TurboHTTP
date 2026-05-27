using Microsoft.AspNetCore.Http;

namespace Servus.Akka.AspNetCore;

internal sealed class EntityResponseMapperCollection
{
    private readonly List<(Type Type, Func<HttpContext, object, Task> Mapper)> _mappers = [];

    internal int Count => _mappers.Count;

    public void Add<T>(Func<HttpContext, T, Task> mapper)
    {
        _mappers.Add((typeof(T), (ctx, obj) => mapper(ctx, (T)obj)));
    }

    public Func<HttpContext, object, Task>? FindMapper(Type responseType)
    {
        foreach (var (type, mapper) in _mappers)
        {
            if (type == responseType)
            {
                return mapper;
            }
        }

        foreach (var (type, mapper) in _mappers)
        {
            if (type.IsAssignableFrom(responseType))
            {
                return mapper;
            }
        }

        return null;
    }
}
